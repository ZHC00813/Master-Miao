using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SWBodyOrganizer
{
    internal sealed class SolidWorksSessionContext
    {
        public bool WasRunning;
        public bool OwnsApplication;
        public bool NativeShutdownComplete;
        public int ProcessId;
        public long ProcessStartTimeUtcTicks;
        public bool OriginalVisible;
        public bool OriginalUserControl;
        public bool OriginalCommandInProgress;
        public bool KeepApplicationOpen;
        public string OriginalActiveTitle = string.Empty;
        public string OriginalActivePath = string.Empty;
    }

    internal sealed class SolidWorksInterferenceException : InvalidOperationException
    {
        public SolidWorksInterferenceException(string message) : base(message) { }
    }

    internal static class WorkerMain
    {
        public static int Run(string requestPath, string responsePath)
        {
            WorkerResponse response = new WorkerResponse();
            WorkerRequest request = null;
            ISldWorks app = null;
            SolidWorksSessionContext session = null;
            try
            {
                request = JsonFile.Load<WorkerRequest>(requestPath);
                app = StartSolidWorks(request, out session);
                response.SolidWorksRevision = app.RevisionNumber() ?? string.Empty;
                response.TemplatePath = FindPartTemplate(app);
                response.AssemblyTemplatePath = FindAssemblyTemplate(app);

                if (string.Equals(request.Operation, "detect", StringComparison.OrdinalIgnoreCase))
                {
                    response.Success = !string.IsNullOrWhiteSpace(response.TemplatePath);
                    if (response.Success)
                    {
                        string stepExecutable;
                        string stepMacro = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MasterMiao.StepMacro.dll");
                        response.StepAvailable = AssemblyStepExporter.TryFindSolidWorksExecutable(out stepExecutable) && File.Exists(stepMacro);
                        response.StepDiagnostic = response.StepAvailable
                            ? "已检测到可见会话与编译型 STEP 宏：" + stepExecutable
                            : "未找到 SLDWORKS.exe 或发行包缺少 MasterMiao.StepMacro.dll。";
                        response.Message = response.StepAvailable
                            ? "SolidWorks 自动化接口、模板与装配体批量 STEP 导出入口均可用。"
                            : "SolidWorks 自动化接口可用，但未找到 STEP 批量导出入口；SLDPRT 与装配体功能仍可使用。";
                    }
                    else response.Message = "SolidWorks 可启动，但未找到可用的零件模板。";
                }
                else if (string.Equals(request.Operation, "scan", StringComparison.OrdinalIgnoreCase))
                {
                    Scan(app, request, response);
                    if (ShouldKeepScanSession(request, response))
                    {
                        response.RetainedSourceDocumentCount = VerifyRetainedSourceDocuments(app, response.Sources);
                        session.KeepApplicationOpen = true;
                        response.SolidWorksKeptOpen = true;
                    }
                }
                else if (string.Equals(request.Operation, "export", StringComparison.OrdinalIgnoreCase))
                {
                    Export(app, request, response);
                    if (request.ExportSettings.ExportStep)
                        AssemblyStepExporter.Export(request, response, session, app);
                }
                else throw new InvalidOperationException("未知工作类型：" + request.Operation);
            }
            catch (OperationCanceledException)
            {
                response.Cancelled = true;
                response.Success = false;
                response.Message = "操作已取消。";
            }
            catch (SolidWorksInterferenceException ex)
            {
                response.Success = false;
                response.Message = ex.Message;
                Emit("PROGRESS", 0, "检测到 SolidWorks 干扰", ex.Message);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = ex.Message;
                Emit("ERROR", 0, "失败", ex.Message);
            }
            finally
            {
                ShutdownSolidWorks(ref app, session);
                try { JsonFile.Save(responsePath, response); } catch { }
            }
            return response.Success ? 0 : (response.Cancelled ? 2 : 1);
        }

        internal static bool ShouldKeepScanSession(WorkerRequest request, WorkerResponse response)
        {
            return request != null && response != null &&
                string.Equals(request.Operation, "scan", StringComparison.OrdinalIgnoreCase) &&
                request.KeepSourceDocumentsOpen && response.Success;
        }

        internal static bool ShouldKeepScannedDocument(WorkerRequest request, SourceRecord source)
        {
            return request != null && source != null && request.KeepSourceDocumentsOpen &&
                string.Equals(source.Status, "读取完成", StringComparison.Ordinal);
        }

        private static int VerifyRetainedSourceDocuments(ISldWorks app, IEnumerable<SourceRecord> sources)
        {
            int retained = 0;
            foreach (SourceRecord source in (sources ?? Enumerable.Empty<SourceRecord>()).Where(item => item.Status == "读取完成"))
            {
                IModelDoc2 model = null;
                try
                {
                    model = app.GetOpenDocumentByName(source.Path) as IModelDoc2;
                    if (model == null) throw new InvalidOperationException("读取完成后未能在 SolidWorks 中保留源文件：" + source.Path);
                    retained++;
                }
                finally { Release(model); }
            }
            return retained;
        }

        private static void ShutdownSolidWorks(ref ISldWorks app, SolidWorksSessionContext session)
        {
            if (session == null || session.NativeShutdownComplete) return;
            if (app != null)
            {
                if (session.OwnsApplication && session.KeepApplicationOpen)
                {
                    try { app.CommandInProgress = false; } catch { }
                    try { app.UserControl = true; } catch { }
                    try { app.Visible = true; } catch { }
                }
                else if (session.OwnsApplication)
                {
                    try { app.CommandInProgress = false; } catch { }
                    try { app.CloseAllDocuments(true); } catch { }
                    try { app.ExitApp(); } catch { }
                }
                else RestoreUserSession(app, session);
                Release(app);
                app = null;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (session.OwnsApplication && !session.KeepApplicationOpen) StopOwnedProcess(session.ProcessId, session.ProcessStartTimeUtcTicks);
            session.NativeShutdownComplete = true;
        }

        private static ISldWorks StartSolidWorks(WorkerRequest request, out SolidWorksSessionContext session)
        {
            session = new SolidWorksSessionContext();
            Process[] existingProcesses = Process.GetProcessesByName("SLDWORKS");
            HashSet<int> existingProcessIds;
            try { existingProcessIds = new HashSet<int>(existingProcesses.Select(item => item.Id)); }
            finally { foreach (Process process in existingProcesses) process.Dispose(); }
            Type type = Type.GetTypeFromProgID("SldWorks.Application");
            if (type == null) throw new InvalidOperationException("没有检测到已注册的 SolidWorks 自动化接口。");
            ISldWorks app = null;
            try
            {
                if (existingProcessIds.Count > 1)
                    throw new InvalidOperationException("检测到多个 SolidWorks 进程。为避免连接到错误窗口，请只保留需要复用的一个 SolidWorks 会话后重试。");
                object value;
                bool authorizedLaunch = request.AuthorizedSolidWorksProcessId > 0;
                if (authorizedLaunch)
                {
                    session.ProcessId = request.AuthorizedSolidWorksProcessId;
                    session.ProcessStartTimeUtcTicks = request.AuthorizedSolidWorksStartTimeUtcTicks;
                    session.OwnsApplication = true;
                    session.WasRunning = false;
                    if (!existingProcessIds.SetEquals(new[] { session.ProcessId }))
                        throw new InvalidOperationException("用户授权启动的 SolidWorks 进程与当前检测结果不一致，本次任务已停止。");
                    using (Process process = Process.GetProcessById(session.ProcessId))
                        if (process.StartTime.ToUniversalTime().Ticks != session.ProcessStartTimeUtcTicks)
                            throw new InvalidOperationException("用户授权启动的 SolidWorks 进程身份校验失败，本次任务已停止。");
                    Emit("PROGRESS", 1, "启动", "正在连接用户已授权打开的 SolidWorks 界面");
                    value = WaitForActiveSolidWorks(session.ProcessId, request.CancelFile);
                }
                else if (existingProcessIds.Count == 1)
                {
                    Emit("PROGRESS", 1, "连接", "正在连接用户已打开的 SolidWorks 会话");
                    value = WaitForActiveSolidWorks(existingProcessIds.Single(), request.CancelFile);
                    session.WasRunning = true;
                }
                else
                {
                    Emit("PROGRESS", 1, "启动", "正在启动隔离的 SolidWorks 工作实例");
                    value = Activator.CreateInstance(type);
                }
                if (value == null) throw new InvalidOperationException("无法启动 SolidWorks。");
                app = (ISldWorks)value;
                int processId = app.GetProcessID();
                if ((session.WasRunning || authorizedLaunch) && !existingProcessIds.Contains(processId))
                    throw new InvalidOperationException("SolidWorks 活动对象与检测到的用户窗口不一致，本次操作已停止。");
                session.ProcessId = processId;
                session.OwnsApplication = authorizedLaunch || !session.WasRunning;
                try
                {
                    using (Process process = Process.GetProcessById(processId))
                        session.ProcessStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                }
                catch
                {
                    if (session.OwnsApplication) throw new InvalidOperationException("无法确认程序所启动的 SolidWorks 进程身份，本次任务已停止。");
                }
                session.OriginalVisible = app.Visible;
                session.OriginalUserControl = app.UserControl;
                session.OriginalCommandInProgress = app.CommandInProgress;
                CaptureActiveDocument(app, session);
                if (session.OwnsApplication)
                {
                    app.Visible = true;
                    app.UserControl = true;
                }
                app.CommandInProgress = true;
                Emit("PROGRESS", 2, session.WasRunning ? "连接" : "启动", (session.WasRunning ? "用户会话已保护" : "隔离实例已确认") + "，进程 " + processId);
                return app;
            }
            catch
            {
                Release(app);
                throw;
            }
        }

        private static object WaitForActiveSolidWorks(int expectedProcessId, string cancelFile)
        {
            Exception lastError = null;
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(90))
            {
                CheckCancellation(cancelFile);
                ISldWorks candidate = null;
                try
                {
                    candidate = Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                    if (candidate != null && candidate.GetProcessID() == expectedProcessId) return candidate;
                }
                catch (Exception ex) { lastError = ex; }
                Release(candidate);
                Thread.Sleep(500);
            }
            throw new InvalidOperationException("SolidWorks 界面已启动，但 90 秒内未能连接自动化接口。请确认程序与 SolidWorks 使用相同权限运行。", lastError);
        }

        private static void CaptureActiveDocument(ISldWorks app, SolidWorksSessionContext session)
        {
            IModelDoc2 active = null;
            try
            {
                active = app.ActiveDoc as IModelDoc2;
                if (active == null) return;
                session.OriginalActiveTitle = active.GetTitle() ?? string.Empty;
                session.OriginalActivePath = active.GetPathName() ?? string.Empty;
            }
            catch { }
            finally { Release(active); }
        }

        private static void RestoreUserSession(ISldWorks app, SolidWorksSessionContext session)
        {
            try
            {
                string title = session.OriginalActiveTitle;
                IModelDoc2 original = null;
                if (!string.IsNullOrWhiteSpace(session.OriginalActivePath))
                    try { original = app.GetOpenDocumentByName(session.OriginalActivePath) as IModelDoc2; } catch { }
                if (original != null) title = original.GetTitle();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    int errors = 0;
                    app.ActivateDoc3(title, false, 0, ref errors);
                }
                Release(original);
            }
            catch { }
            try { app.CommandInProgress = session.OriginalCommandInProgress; } catch { }
            try { app.UserControl = session.OriginalUserControl; } catch { }
            try { app.Visible = session.OriginalVisible; } catch { }
        }

        internal static void EnsureActiveDocument(ISldWorks app, string expectedTitle, string operation)
        {
            IModelDoc2 active = app.ActiveDoc as IModelDoc2;
            string actualTitle = active == null ? string.Empty : (active.GetTitle() ?? string.Empty);
            if (active == null || !string.Equals(actualTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
                throw new SolidWorksInterferenceException("检测到 SolidWorks 活动文档在“" + operation + "”期间发生变化。为保护输出，本次任务已停止；请不要在读取或导出过程中切换、关闭或编辑 SolidWorks 文档。");
        }

        private static void StopOwnedProcess(int processId, long expectedStartTimeUtcTicks)
        {
            if (processId <= 0 || expectedStartTimeUtcTicks <= 0) return;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (process.StartTime.ToUniversalTime().Ticks != expectedStartTimeUtcTicks) return;
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
            }
            catch (ArgumentException) { }
            catch { }
        }

        private static string FindPartTemplate(ISldWorks app)
        {
            string template = string.Empty;
            try { template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart); } catch { }
            if (!string.IsNullOrWhiteSpace(template) && File.Exists(template)) return template;
            string[] known =
            {
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_part.prtdot",
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot",
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2023\templates\gb_part.prtdot"
            };
            return known.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static string FindAssemblyTemplate(ISldWorks app)
        {
            string template = string.Empty;
            try { template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly); } catch { }
            if (!string.IsNullOrWhiteSpace(template) && File.Exists(template)) return template;
            string[] known =
            {
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_assembly.asmdot",
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_assembly.asmdot",
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2023\templates\gb_assembly.asmdot"
            };
            return known.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static void Scan(ISldWorks app, WorkerRequest request, WorkerResponse response)
        {
            if (request.Sources == null || request.Sources.Count == 0) throw new InvalidOperationException("没有需要读取的源文件。");
            if (string.IsNullOrWhiteSpace(response.TemplatePath)) throw new InvalidOperationException("没有找到可用的 SolidWorks 零件模板。");
            List<SourceRecord> scanned = new List<SourceRecord>();

            for (int fileIndex = 0; fileIndex < request.Sources.Count; fileIndex++)
            {
                CheckCancellation(request.CancelFile);
                SourceRecord input = request.Sources[fileIndex];
                SourceRecord source = new SourceRecord
                {
                    Id = string.IsNullOrWhiteSpace(input.Id) ? Guid.NewGuid().ToString("N") : input.Id,
                    Path = input.Path,
                    Name = Path.GetFileName(input.Path),
                    Status = "正在读取"
                };
                scanned.Add(source);
                IModelDoc2 model = null;
                IPartDoc part = null;
                string title = string.Empty;
                bool wasAlreadyOpen = false;
                try
                {
                    FileInfo info = new FileInfo(source.Path);
                    if (!info.Exists) throw new FileNotFoundException("源文件不存在。", source.Path);
                    source.Length = info.Length;
                    source.LastWriteTicks = info.LastWriteTimeUtc.Ticks;
                    int openErrors = 0, openWarnings = 0;
                    int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
                    Emit("PROGRESS", Percent(fileIndex, request.Sources.Count, 5), "读取文件", source.Name);
                    model = app.GetOpenDocumentByName(source.Path) as IModelDoc2;
                    wasAlreadyOpen = model != null;
                    if (model == null) model = app.OpenDoc6(source.Path, (int)swDocumentTypes_e.swDocPART, options, string.Empty, ref openErrors, ref openWarnings);
                    if (model == null) throw new InvalidOperationException(string.Format("无法打开文件，错误={0}，警告={1}。", openErrors, openWarnings));
                    title = model.GetTitle();
                    try { source.Configuration = model.ConfigurationManager.ActiveConfiguration.Name; } catch { source.Configuration = string.Empty; }
                    part = (IPartDoc)model;
                    object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];
                    source.BodyCount = bodies.Length;
                    source.Bodies = new List<BodyRecord>();
                    string sourceCache = Path.Combine(request.CacheRoot, NameRules.ShortHash(source.Path + "|" + source.LastWriteTicks));
                    Directory.CreateDirectory(sourceCache);

                    for (int index = 0; index < bodies.Length; index++)
                    {
                        CheckCancellation(request.CancelFile);
                        IBody2 body = (IBody2)bodies[index];
                        int overall = Percent(fileIndex + ((index + 1.0) / Math.Max(1, bodies.Length)), request.Sources.Count, 5);
                        Emit("PROGRESS", overall, "生成预览", string.Format("{0}：实体 {1}/{2}", source.Name, index + 1, bodies.Length));
                        string originalName = body.Name ?? ("实体" + (index + 1));
                        BodyRecord item = new BodyRecord
                        {
                            SourceId = source.Id,
                            SourcePath = source.Path,
                            SourceName = source.Name,
                            Index = index,
                            OriginalName = originalName,
                            ExportName = string.Format("{0:D3}_{1}", index + 1, NameRules.SafeStem(originalName, "实体" + (index + 1))),
                            CategoryId = CategoryNode.UnclassifiedId,
                            ExportSelected = true,
                            GeometryKey = BuildGeometryKey(body),
                            Status = "已读取"
                        };
                        if (request.GeneratePreviews)
                        {
                            try
                            {
                                string suffix = item.GeometryKey.Substring(0, Math.Min(12, item.GeometryKey.Length));
                                string bodyCache = Path.Combine(sourceCache, string.Format("{0:D4}_{1}", index + 1, suffix));
                                Directory.CreateDirectory(bodyCache);
                                GeneratePreviews(app, model, body, response.TemplatePath, bodyCache, item);
                            }
                            catch (Exception previewError)
                            {
                                if (previewError is SolidWorksInterferenceException) throw;
                                item.Status = "预览失败";
                                item.Message = previewError.Message;
                            }
                        }
                        source.Bodies.Add(item);
                        Release(body);
                    }
                    source.Status = "读取完成";
                    source.Message = openWarnings == 0 ? string.Empty : "SolidWorks 打开警告：" + openWarnings;
                }
                catch (Exception fileError)
                {
                    if (fileError is SolidWorksInterferenceException) throw;
                    source.Status = "读取失败";
                    source.Message = fileError.Message;
                }
                finally
                {
                    Release(part);
                    bool keepOpen = ShouldKeepScannedDocument(request, source);
                    if (model != null && !wasAlreadyOpen && !keepOpen) { try { app.CloseDoc(string.IsNullOrWhiteSpace(title) ? model.GetTitle() : title); } catch { } }
                    Release(model);
                }
            }

            AssignDuplicateGroups(scanned.SelectMany(item => item.Bodies).ToList());
            response.Sources = scanned;
            response.Success = scanned.Any(item => item.Status == "读取完成");
            response.Message = string.Format("读取完成：{0}/{1} 个文件成功。", scanned.Count(item => item.Status == "读取完成"), scanned.Count) +
                (response.Success && request.KeepSourceDocumentsOpen ? " 已读取的源文件已在 SolidWorks 中保持打开，可直接使用定位功能。" : string.Empty);
            Emit("PROGRESS", 100, "完成", response.Message);
        }

        private static void GeneratePreviews(ISldWorks app, IModelDoc2 sourceModel, IBody2 sourceBody, string template, string folder, BodyRecord item)
        {
            string front = Path.Combine(folder, "front.png");
            string top = Path.Combine(folder, "top.png");
            string iso = Path.Combine(folder, "iso.png");
            if (File.Exists(front) && File.Exists(top) && File.Exists(iso))
            {
                item.PreviewFront = front;
                item.PreviewTop = top;
                item.PreviewIso = iso;
                return;
            }
            IBody2 copy = null;
            IModelDoc2 target = null;
            IPartDoc targetPart = null;
            object feature = null;
            string targetTitle = string.Empty;
            try
            {
                copy = sourceBody.Copy() as IBody2;
                if (copy == null) throw new InvalidOperationException("无法复制实体几何。");
                target = app.NewDocument(template, 0, 0.0, 0.0) as IModelDoc2;
                if (target == null) throw new InvalidOperationException("无法创建预览零件。");
                targetTitle = target.GetTitle();
                targetPart = (IPartDoc)target;
                feature = targetPart.CreateFeatureFromBody3(copy, false, 0);
                if (feature == null) throw new InvalidOperationException("无法在预览零件中建立实体。");
                target.ForceRebuild3(false);
                int activateErrors = 0;
                app.ActivateDoc3(targetTitle, false, 0, ref activateErrors);
                EnsureActiveDocument(app, targetTitle, "生成实体预览");
                SaveViewPng(target, (int)swStandardViews_e.swFrontView, front);
                EnsureActiveDocument(app, targetTitle, "生成实体预览");
                SaveViewPng(target, (int)swStandardViews_e.swTopView, top);
                EnsureActiveDocument(app, targetTitle, "生成实体预览");
                SaveViewPng(target, (int)swStandardViews_e.swIsometricView, iso);
                item.PreviewFront = front;
                item.PreviewTop = top;
                item.PreviewIso = iso;
            }
            finally
            {
                if (target != null) { try { app.CloseDoc(string.IsNullOrWhiteSpace(targetTitle) ? target.GetTitle() : targetTitle); } catch { } }
                Release(feature);
                Release(targetPart);
                Release(target);
                Release(copy);
                if (sourceModel != null)
                {
                    int activateErrors = 0;
                    try { app.ActivateDoc3(sourceModel.GetTitle(), false, 0, ref activateErrors); } catch { }
                }
            }
        }

        private static void SaveViewPng(IModelDoc2 model, int view, string pngPath)
        {
            string bmpPath = Path.ChangeExtension(pngPath, ".bmp");
            model.ShowNamedView2(string.Empty, view);
            model.ViewZoomtofit2();
            Thread.Sleep(80);
            if (!model.SaveBMP(bmpPath, 420, 300)) throw new InvalidOperationException("无法保存预览图。");
            using (Image image = Image.FromFile(bmpPath))
            using (Bitmap bitmap = new Bitmap(image)) bitmap.Save(pngPath, ImageFormat.Png);
            File.Delete(bmpPath);
        }

        private static string BuildGeometryKey(IBody2 body)
        {
            List<string> faceTokens = new List<string>();
            object[] faces = body.GetFaces() as object[];
            if (faces != null)
            {
                foreach (object faceObject in faces)
                {
                    IFace2 face = faceObject as IFace2;
                    ISurface surface = null;
                    try
                    {
                        surface = face == null ? null : face.IGetSurface();
                        int identity = surface == null ? -1 : surface.Identity();
                        faceTokens.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1:R}:{2}", identity, Math.Round(face.GetArea(), 10), face.GetLoopCount()));
                    }
                    catch { faceTokens.Add("?"); }
                    finally { Release(surface); Release(face); }
                }
            }
            faceTokens.Sort(StringComparer.Ordinal);
            double[] mass = body.GetMassProperties(1.0) as double[];
            double volume = mass != null && mass.Length > 3 ? mass[3] : 0.0;
            double area = mass != null && mass.Length > 4 ? mass[4] : 0.0;
            string raw = string.Format(CultureInfo.InvariantCulture, "V={0:R}|A={1:R}|F={2}|E={3}||{4}", Math.Round(volume, 10), Math.Round(area, 10), body.GetFaceCount(), body.GetEdgeCount(), string.Join(";", faceTokens.ToArray()));
            return NameRules.ShortHash(raw);
        }

        private static void AssignDuplicateGroups(List<BodyRecord> bodies)
        {
            int group = 0;
            foreach (IGrouping<string, BodyRecord> items in bodies.Where(item => !string.IsNullOrWhiteSpace(item.GeometryKey)).GroupBy(item => item.GeometryKey))
            {
                if (items.Count() < 2) continue;
                group++;
                string label = "重复组 " + group.ToString("D2");
                foreach (BodyRecord item in items) item.DuplicateGroup = label;
            }
        }

        private static void Export(ISldWorks app, WorkerRequest request, WorkerResponse response)
        {
            if (request.ExportItems == null || request.ExportItems.Count == 0) throw new InvalidOperationException("没有需要导出的实体。");
            if (string.IsNullOrWhiteSpace(request.OutputRoot)) throw new InvalidOperationException("没有指定输出目录。");
            if (string.IsNullOrWhiteSpace(request.StagingRoot)) throw new InvalidOperationException("没有指定隔离暂存目录。");
            request.OutputRoot = Path.GetFullPath(request.OutputRoot);
            request.StagingRoot = Path.GetFullPath(request.StagingRoot);
            if (!request.ExportSettings.ExportSldprt && !request.ExportSettings.ExportStep) throw new InvalidOperationException("至少需要选择一种导出格式。");
            if (request.ExportSettings.ExportStep && !request.ExportSettings.ExportSldprt) throw new InvalidOperationException("装配体批量 STEP 导出需要同时导出 SLDPRT 零件。");
            if (request.ExportSettings.CreateAssembly && !request.ExportSettings.ExportSldprt) throw new InvalidOperationException("生成装配体时必须同时导出 SLDPRT 零件。");
            bool needAssembly = request.ExportSettings.CreateAssembly || request.ExportSettings.ExportStep;
            if (needAssembly && string.IsNullOrWhiteSpace(response.AssemblyTemplatePath)) throw new InvalidOperationException("没有找到可用的 SolidWorks 装配体模板。");
            if (string.IsNullOrWhiteSpace(response.TemplatePath)) throw new InvalidOperationException("没有找到可用的 SolidWorks 零件模板。");
            Directory.CreateDirectory(request.StagingRoot);
            List<ExportResultItem> results = new List<ExportResultItem>();
            int completed = 0;

            foreach (IGrouping<string, ExportPlanItem> sourceGroup in request.ExportItems.GroupBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase))
            {
                List<ExportPlanItem> groupPlans = sourceGroup.ToList();
                CheckCancellation(request.CancelFile);
                IModelDoc2 sourceModel = null;
                IPartDoc sourcePart = null;
                object[] bodies = null;
                string sourceTitle = string.Empty;
                bool sourceWasAlreadyOpen = false;
                try
                {
                    int openErrors = 0, openWarnings = 0;
                    int openOptions = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
                    sourceModel = app.GetOpenDocumentByName(sourceGroup.Key) as IModelDoc2;
                    sourceWasAlreadyOpen = sourceModel != null;
                    if (sourceModel == null) sourceModel = app.OpenDoc6(sourceGroup.Key, (int)swDocumentTypes_e.swDocPART, openOptions, string.Empty, ref openErrors, ref openWarnings);
                    if (sourceModel == null) throw new InvalidOperationException("无法重新打开源文件：" + sourceGroup.Key);
                    sourceTitle = sourceModel.GetTitle();
                    sourcePart = (IPartDoc)sourceModel;
                    bodies = sourcePart.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];

                    foreach (ExportPlanItem plan in groupPlans)
                    {
                        CheckCancellation(request.CancelFile);
                        completed++;
                        Emit("PROGRESS", Percent(completed, request.ExportItems.Count, 0), "导出零件", string.Format("{0}/{1}：{2}", completed, request.ExportItems.Count, plan.ExportName));
                        ExportResultItem result = CreateResult(plan);
                        results.Add(result);
                        if (plan.BodyIndex < 0 || plan.BodyIndex >= bodies.Length)
                        {
                            result.Message = "源实体序号已经变化，请重新扫描。";
                            result.SldprtStatus = request.ExportSettings.ExportSldprt ? "失败" : "未启用";
                            result.StepStatus = request.ExportSettings.ExportStep ? "失败" : "未启用";
                            continue;
                        }
                        try { ExportOne(app, (IBody2)bodies[plan.BodyIndex], response.TemplatePath, request, plan, result); }
                        catch (Exception itemError)
                        {
                            if (itemError is SolidWorksInterferenceException) throw;
                            result.Message = itemError.Message;
                            if (request.ExportSettings.ExportSldprt && result.SldprtStatus == "未启用") result.SldprtStatus = "失败";
                            if (request.ExportSettings.ExportStep && result.StepStatus == "未启用") result.StepStatus = "失败";
                        }
                    }
                    if (needAssembly)
                    {
                        Emit("PROGRESS", Percent(completed, request.ExportItems.Count, 0), "生成装配体", Path.GetFileNameWithoutExtension(sourceGroup.Key));
                        List<ExportResultItem> sourceResults = results.Where(item => string.Equals(item.SourcePath, sourceGroup.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                        AssemblyResultItem assemblyResult = CreateAssemblyForSource(app, response.AssemblyTemplatePath, request, sourceGroup.Key, sourceResults, request.ExportSettings.CreateAssembly, request.ExportSettings.ExportStep);
                        response.AssemblyResults.Add(assemblyResult);
                        foreach (ExportResultItem item in sourceResults)
                        {
                            if (request.ExportSettings.CreateAssembly)
                            {
                                item.AssemblyPath = assemblyResult.AssemblyPath;
                                item.AssemblyStatus = assemblyResult.Status;
                            }
                            if (!IsSuccessful(assemblyResult.Status) && string.IsNullOrWhiteSpace(item.Message)) item.Message = assemblyResult.Message;
                        }
                    }
                }
                catch (Exception sourceError)
                {
                    if (sourceError is SolidWorksInterferenceException) throw;
                    foreach (ExportPlanItem plan in groupPlans)
                    {
                        if (results.Any(item => item.BodyId == plan.BodyId)) continue;
                        ExportResultItem result = CreateResult(plan);
                        result.Message = sourceError.Message;
                        result.SldprtStatus = request.ExportSettings.ExportSldprt ? "失败" : "未启用";
                        result.StepStatus = request.ExportSettings.ExportStep ? "失败" : "未启用";
                        results.Add(result);
                    }
                    if (needAssembly && !response.AssemblyResults.Any(item => string.Equals(item.SourcePath, sourceGroup.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        response.AssemblyResults.Add(new AssemblyResultItem
                        {
                            SourcePath = sourceGroup.Key,
                            SourceName = Path.GetFileName(sourceGroup.Key),
                            Status = "失败",
                            Message = sourceError.Message
                        });
                    }
                }
                finally
                {
                    if (bodies != null) foreach (object body in bodies) Release(body);
                    Release(sourcePart);
                    if (sourceModel != null && !sourceWasAlreadyOpen) { try { app.CloseDoc(string.IsNullOrWhiteSpace(sourceTitle) ? sourceModel.GetTitle() : sourceTitle); } catch { } }
                    Release(sourceModel);
                }
            }

            foreach (AssemblyResultItem assemblyResult in response.AssemblyResults)
                foreach (ExportResultItem item in results.Where(value => string.Equals(value.SourcePath, assemblyResult.SourcePath, StringComparison.OrdinalIgnoreCase)))
                {
                    if (request.ExportSettings.CreateAssembly)
                    {
                        item.AssemblyPath = assemblyResult.AssemblyPath;
                        item.AssemblyStatus = assemblyResult.Status;
                    }
                }
            response.ExportResults = results;
            int successful = results.Count(item => (!request.ExportSettings.ExportSldprt || IsSuccessful(item.SldprtStatus)) && (!request.ExportSettings.ExportStep || IsSuccessful(item.StepStatus)));
            int successfulAssemblies = response.AssemblyResults.Count(item => IsSuccessful(item.Status));
            response.Success = successful == results.Count && (!request.ExportSettings.CreateAssembly || successfulAssemblies == response.AssemblyResults.Count);
            response.Message = request.ExportSettings.ExportStep
                ? string.Format("零件与装配体已就绪，正在启动可见 SolidWorks 批量导出 STEP（{0} 项）。", results.Count)
                : request.ExportSettings.CreateAssembly
                ? string.Format("导出完成：零件 {0}/{1} 项成功，装配体 {2}/{3} 个成功。", successful, results.Count, successfulAssemblies, response.AssemblyResults.Count)
                : string.Format("导出完成：{0}/{1} 项成功。", successful, results.Count);
            Emit("PROGRESS", request.ExportSettings.ExportStep ? 72 : 100, request.ExportSettings.ExportStep ? "准备 STEP" : "完成", response.Message);
        }

        internal static bool IsSuccessful(string status)
        {
            return status == "成功" || status.StartsWith("跳过", StringComparison.Ordinal);
        }

        private static ExportResultItem CreateResult(ExportPlanItem plan)
        {
            return new ExportResultItem
            {
                BodyId = plan.BodyId,
                SourcePath = plan.SourcePath,
                SourceName = plan.SourceName,
                OriginalName = plan.OriginalName,
                ExportName = plan.ExportName,
                CategoryPath = plan.CategoryPath,
                PreviewFront = plan.PreviewFront,
                PreviewTop = plan.PreviewTop,
                PreviewIso = plan.PreviewIso,
                Quantity = plan.Quantity
            };
        }

        private static void ExportOne(ISldWorks app, IBody2 body, string template, WorkerRequest request, ExportPlanItem plan, ExportResultItem result)
        {
            IBody2 copy = null;
            IModelDoc2 target = null;
            IPartDoc targetPart = null;
            IModelDocExtension extension = null;
            object feature = null;
            string targetTitle = string.Empty;
            string safeName = NameRules.SafeStem(plan.ExportName, "零件");
            string relativeFolder = SafeRelativeFolder(plan.CategoryPath);
            string partRoot = GetPartOutputRoot(request);
            string stepRoot = GetStepOutputRoot(request);
            string outputFolder = string.IsNullOrWhiteSpace(relativeFolder) ? partRoot : Path.Combine(partRoot, relativeFolder);
            string stepFolder = string.IsNullOrWhiteSpace(relativeFolder) ? stepRoot : Path.Combine(stepRoot, relativeFolder);
            Directory.CreateDirectory(outputFolder);
            if (request.ExportSettings.ExportStep) Directory.CreateDirectory(stepFolder);
            string resolvedStem = ResolveOutputStem(outputFolder, stepFolder, safeName, request.ExportSettings);
            string finalBase = Path.Combine(outputFolder, resolvedStem);
            string finalSldprt = finalBase + ".SLDPRT";
            string finalStep = Path.Combine(stepFolder, resolvedStem + ".STEP");
            bool existingSldprt = File.Exists(finalSldprt);
            bool existingStep = File.Exists(finalStep);
            if (request.ExportSettings.ExportSldprt)
            {
                result.SldprtPath = finalSldprt;
                if (existingSldprt && request.ExportSettings.ConflictPolicy == "跳过") result.SldprtStatus = "跳过（已存在）";
            }
            if (request.ExportSettings.ExportStep)
            {
                result.StepPath = finalStep;
                result.StepStatus = existingStep && request.ExportSettings.ConflictPolicy == "跳过" ? "跳过（已存在）" : "待批量导出";
            }
            if (existingSldprt && request.ExportSettings.ConflictPolicy == "跳过")
            {
                result.VerificationStatus = "未重新验证";
                return;
            }

            string token = Guid.NewGuid().ToString("N");
            string stageSldprt = Path.Combine(request.StagingRoot, token + ".SLDPRT");
            try
            {
                try
                {
                    copy = body.Copy() as IBody2;
                    if (copy == null) throw new InvalidOperationException("无法复制实体几何。");
                    target = app.NewDocument(template, 0, 0.0, 0.0) as IModelDoc2;
                    if (target == null) throw new InvalidOperationException("无法创建独立零件。");
                    targetTitle = target.GetTitle();
                    targetPart = (IPartDoc)target;
                    feature = targetPart.CreateFeatureFromBody3(copy, false, 0);
                    if (feature == null) throw new InvalidOperationException("无法在新零件中建立实体。");
                    target.ForceRebuild3(false);
                    int activateErrors = 0;
                    app.ActivateDoc3(targetTitle, false, 0, ref activateErrors);
                    EnsureActiveDocument(app, targetTitle, "保存拆分零件");
                    extension = target.Extension;
                    int errors = 0, warnings = 0;
                    bool saved = extension.SaveAs(stageSldprt, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                    targetTitle = target.GetTitle();
                    EnsureActiveDocument(app, targetTitle, "保存拆分零件");
                    if (!saved || !File.Exists(stageSldprt) || new FileInfo(stageSldprt).Length == 0) throw new InvalidOperationException(string.Format("SLDPRT 保存失败，错误={0}，警告={1}。", errors, warnings));
                    if (request.ExportSettings.ExportSldprt) result.SldprtStatus = "已生成";
                }
                finally
                {
                    if (target != null) { try { app.CloseDoc(target.GetTitle()); } catch { try { app.CloseDoc(targetTitle); } catch { } } }
                    Release(extension);
                    Release(feature);
                    Release(targetPart);
                    Release(target);
                    Release(copy);
                }

                VerifySingleBody(app, stageSldprt);
                result.VerificationStatus = "单实体验证通过";

                if (request.ExportSettings.ConflictPolicy == "覆盖")
                {
                    BackupExisting(finalBase, new ExportSettings
                    {
                        ExportSldprt = request.ExportSettings.ExportSldprt,
                        ExportStep = false
                    });
                }
                if (request.ExportSettings.ExportSldprt)
                {
                    result.SldprtPath = finalSldprt;
                    File.Copy(stageSldprt, result.SldprtPath, false);
                    result.SldprtStatus = "成功";
                }
            }
            finally
            {
                TryDelete(stageSldprt);
            }
        }

        private static AssemblyResultItem CreateAssemblyForSource(ISldWorks app, string assemblyTemplate, WorkerRequest request, string sourcePath, List<ExportResultItem> sourceResults, bool keepAssembly, bool neededForStep)
        {
            AssemblyResultItem result = new AssemblyResultItem
            {
                SourcePath = sourcePath,
                SourceName = Path.GetFileName(sourcePath),
                Status = "失败",
                StepStatus = neededForStep ? "待批量导出" : "未启用",
                Temporary = !keepAssembly
            };
            string finalPath = keepAssembly ? ResolveAssemblyPath(GetPartOutputRoot(request), sourcePath, request.ExportSettings.ConflictPolicy) : string.Empty;
            result.AssemblyPath = finalPath;
            List<string> componentPaths = sourceResults
                .Where(item => IsSuccessful(item.SldprtStatus) && !string.IsNullOrWhiteSpace(item.SldprtPath) && File.Exists(item.SldprtPath))
                .Select(item => item.SldprtPath).ToList();
            result.ComponentCount = componentPaths.Count;
            if (componentPaths.Count != sourceResults.Count)
            {
                result.Message = string.Format("装配体未生成：{0}/{1} 个拆分零件可用。", componentPaths.Count, sourceResults.Count);
                result.StepStatus = neededForStep ? "失败" : result.StepStatus;
                return result;
            }
            if (keepAssembly && !neededForStep && File.Exists(finalPath) && request.ExportSettings.ConflictPolicy == "跳过")
            {
                result.Status = "跳过（已存在）";
                result.Message = "目标装配体已存在。";
                return result;
            }

            string stagePath = Path.Combine(request.StagingRoot, Guid.NewGuid().ToString("N") + ".SLDASM");
            IModelDoc2 model = null;
            IAssemblyDoc assembly = null;
            IModelDocExtension extension = null;
            string title = string.Empty;
            bool preserveStage = false;
            try
            {
                model = app.NewDocument(assemblyTemplate, 0, 0.0, 0.0) as IModelDoc2;
                if (model == null) throw new InvalidOperationException("无法创建装配体文档。");
                title = model.GetTitle();
                assembly = (IAssemblyDoc)model;
                object fileNames = componentPaths.ToArray();
                object coordinateNames = Enumerable.Repeat(string.Empty, componentPaths.Count).ToArray();
                object added = assembly.AddComponents3(fileNames, null, coordinateNames);
                object[] components = added as object[];
                if (components == null || components.Length != componentPaths.Count)
                    throw new InvalidOperationException(string.Format("装配体插入组件失败：预期 {0} 个，实际 {1} 个。", componentPaths.Count, components == null ? 0 : components.Length));
                foreach (object componentObject in components)
                {
                    IComponent2 component = componentObject as IComponent2;
                    try
                    {
                        model.ClearSelection2(true);
                        if (component != null && component.Select4(false, null, false)) assembly.FixComponent();
                    }
                    finally { Release(component); }
                }
                model.ClearSelection2(true);
                model.ForceRebuild3(false);
                int activateErrors = 0;
                app.ActivateDoc3(title, false, 0, ref activateErrors);
                EnsureActiveDocument(app, title, "保存原位装配体");
                extension = model.Extension;
                int errors = 0, warnings = 0;
                bool saved = extension.SaveAs(stagePath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                title = model.GetTitle();
                EnsureActiveDocument(app, title, "保存原位装配体");
                if (!saved || !File.Exists(stagePath) || new FileInfo(stagePath).Length == 0)
                    throw new InvalidOperationException(string.Format("装配体保存失败，错误={0}，警告={1}。", errors, warnings));

                Release(extension); extension = null;
                Release(assembly); assembly = null;
                if (model != null) { try { app.CloseDoc(string.IsNullOrWhiteSpace(title) ? model.GetTitle() : title); } catch { } }
                Release(model); model = null;
                VerifyAssembly(app, stagePath, componentPaths);
                if (keepAssembly)
                {
                    if (File.Exists(finalPath) && request.ExportSettings.ConflictPolicy == "跳过")
                    {
                        result.Status = "跳过（已存在）";
                        result.Message = "目标装配体已存在；STEP 将使用本次生成的临时装配体。";
                    }
                    else
                    {
                        if (request.ExportSettings.ConflictPolicy == "覆盖" && File.Exists(finalPath)) BackupExistingAssembly(finalPath);
                        File.Copy(stagePath, finalPath, false);
                        result.Status = "成功";
                        result.Message = "已按原零件坐标插入并固定全部组件。";
                    }
                }
                else
                {
                    result.Status = "成功";
                    result.Message = "已生成用于批量 STEP 导出的临时原位装配体。";
                }

                if (neededForStep)
                {
                    result.StepSourceAssemblyPath = keepAssembly && result.Status == "成功" ? finalPath : stagePath;
                    preserveStage = string.Equals(result.StepSourceAssemblyPath, stagePath, StringComparison.OrdinalIgnoreCase);
                }
                return result;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                result.StepStatus = neededForStep ? "失败" : result.StepStatus;
                return result;
            }
            finally
            {
                Release(extension);
                Release(assembly);
                if (model != null) { try { app.CloseDoc(string.IsNullOrWhiteSpace(title) ? model.GetTitle() : title); } catch { } }
                Release(model);
                if (!preserveStage) TryDelete(stagePath);
            }
        }

        private static void VerifyAssembly(ISldWorks app, string path, List<string> expectedPaths)
        {
            IModelDoc2 model = null;
            IAssemblyDoc assembly = null;
            string title = string.Empty;
            object[] components = null;
            try
            {
                int errors = 0, warnings = 0;
                int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
                model = app.OpenDoc6(path, (int)swDocumentTypes_e.swDocASSEMBLY, options, string.Empty, ref errors, ref warnings);
                if (model == null) throw new InvalidOperationException(string.Format("装配体无法重新打开验证，错误={0}，警告={1}。", errors, warnings));
                title = model.GetTitle();
                int activateErrors = 0;
                app.ActivateDoc3(title, false, 0, ref activateErrors);
                EnsureActiveDocument(app, title, "验证原位装配体");
                assembly = (IAssemblyDoc)model;
                components = assembly.GetComponents(false) as object[] ?? new object[0];
                if (components.Length != expectedPaths.Count)
                    throw new InvalidOperationException(string.Format("装配体验证失败：预期 {0} 个组件，实际 {1} 个。", expectedPaths.Count, components.Length));
                HashSet<string> expected = new HashSet<string>(expectedPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
                HashSet<string> actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int fixedCount = 0;
                foreach (object componentObject in components)
                {
                    IComponent2 component = componentObject as IComponent2;
                    if (component == null) continue;
                    string componentPath = component.GetPathName();
                    if (!string.IsNullOrWhiteSpace(componentPath)) actual.Add(Path.GetFullPath(componentPath));
                    if (component.IsFixed()) fixedCount++;
                }
                if (!expected.SetEquals(actual))
                    throw new InvalidOperationException("装配体验证失败：组件引用与本次导出的零件文件不一致。");
                if (fixedCount != components.Length)
                    throw new InvalidOperationException(string.Format("装配体验证失败：{0}/{1} 个组件已固定。", fixedCount, components.Length));
            }
            finally
            {
                if (components != null) foreach (object component in components) Release(component);
                Release(assembly);
                if (model != null) { try { app.CloseDoc(string.IsNullOrWhiteSpace(title) ? model.GetTitle() : title); } catch { } }
                Release(model);
            }
        }

        private static string ResolveAssemblyPath(string outputRoot, string sourcePath, string conflictPolicy)
        {
            string stem = NameRules.SafeStem(Path.GetFileNameWithoutExtension(sourcePath), "多实体零件") + "_拆分装配体";
            string candidate = Path.Combine(outputRoot, stem + ".SLDASM");
            if (conflictPolicy != "自动编号" || !File.Exists(candidate)) return candidate;
            for (int index = 2; index < 10000; index++)
            {
                candidate = Path.Combine(outputRoot, stem + "_" + index + ".SLDASM");
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException("无法为重复装配体生成可用名称。");
        }

        private static void BackupExistingAssembly(string path)
        {
            string backup = Path.Combine(AppPaths.Backups, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(backup);
            File.Move(path, Path.Combine(backup, Path.GetFileName(path)));
        }

        private static void VerifySingleBody(ISldWorks app, string path)
        {
            IModelDoc2 model = null;
            IPartDoc part = null;
            string title = string.Empty;
            try
            {
                int errors = 0, warnings = 0;
                int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
                model = app.OpenDoc6(path, (int)swDocumentTypes_e.swDocPART, options, string.Empty, ref errors, ref warnings);
                if (model == null) throw new InvalidOperationException("导出文件无法重新打开。");
                title = model.GetTitle();
                int activateErrors = 0;
                app.ActivateDoc3(title, false, 0, ref activateErrors);
                EnsureActiveDocument(app, title, "验证拆分零件");
                part = (IPartDoc)model;
                object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null || bodies.Length != 1) throw new InvalidOperationException("导出文件没有通过单实体验证。");
                foreach (object body in bodies) Release(body);
            }
            finally
            {
                Release(part);
                if (model != null) { try { app.CloseDoc(string.IsNullOrWhiteSpace(title) ? model.GetTitle() : title); } catch { } }
                Release(model);
            }
        }

        private static string SafeRelativeFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "未分类") return "未分类";
            string[] parts = value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return Path.Combine(parts.Select((part, index) => NameRules.SafeStem(part, "分类" + (index + 1))).ToArray());
        }

        internal static string GetPartOutputRoot(WorkerRequest request)
        {
            return request.ExportSettings != null && request.ExportSettings.SeparateStepOutput
                ? Path.Combine(request.OutputRoot, "零件源文件")
                : request.OutputRoot;
        }

        internal static string GetStepOutputRoot(WorkerRequest request)
        {
            return request.ExportSettings != null && request.ExportSettings.SeparateStepOutput
                ? Path.Combine(request.OutputRoot, "STEP生产文件")
                : request.OutputRoot;
        }

        private static string ResolveOutputStem(string partFolder, string stepFolder, string stem, ExportSettings settings)
        {
            if (settings.ConflictPolicy != "自动编号" || !ExistsForSelectedFormats(partFolder, stepFolder, stem, settings)) return stem;
            for (int index = 2; index < 10000; index++)
            {
                string candidate = stem + "_" + index;
                if (!ExistsForSelectedFormats(partFolder, stepFolder, candidate, settings)) return candidate;
            }
            throw new IOException("无法为重复文件生成可用名称。");
        }

        private static bool ExistsForSelectedFormats(string partFolder, string stepFolder, string stem, ExportSettings settings)
        {
            return (settings.ExportSldprt && File.Exists(Path.Combine(partFolder, stem + ".SLDPRT")))
                || (settings.ExportStep && File.Exists(Path.Combine(stepFolder, stem + ".STEP")));
        }

        private static void BackupExisting(string basePath, ExportSettings settings)
        {
            string backup = Path.Combine(AppPaths.Backups, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            if (settings.ExportSldprt && File.Exists(basePath + ".SLDPRT"))
            {
                Directory.CreateDirectory(backup);
                File.Move(basePath + ".SLDPRT", Path.Combine(backup, Path.GetFileName(basePath) + ".SLDPRT"));
            }
            if (settings.ExportStep && File.Exists(basePath + ".STEP"))
            {
                Directory.CreateDirectory(backup);
                File.Move(basePath + ".STEP", Path.Combine(backup, Path.GetFileName(basePath) + ".STEP"));
            }
        }

        internal static void CheckCancellation(string cancelFile)
        {
            if (!string.IsNullOrWhiteSpace(cancelFile) && File.Exists(cancelFile)) throw new OperationCanceledException();
        }

        private static int Percent(double completed, double total, int floor)
        {
            if (total <= 0) return floor;
            return Math.Max(floor, Math.Min(99, (int)Math.Round((completed / total) * (100 - floor)) + floor));
        }

        internal static void Emit(string kind, int percent, string stage, string detail)
        {
            Console.WriteLine(string.Join("\t", new[] { kind, percent.ToString(CultureInfo.InvariantCulture), ToBase64(stage), ToBase64((detail ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")) }));
            Console.Out.Flush();
        }

        private static string ToBase64(string value)
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try { Marshal.FinalReleaseComObject(value); } catch { }
            }
        }
    }
}
