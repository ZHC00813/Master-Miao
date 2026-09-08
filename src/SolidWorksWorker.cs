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
        public Mutex TaskLease;
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
                response.TaskId = request.TaskId;
                response.StartedUtc = DateTime.UtcNow;
                if (request.Operation == "export")
                    response.ExportResults = request.ExportItems.Select(plan => CreateResult(plan, request.ExportSettings)).ToList();
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
                        JsonFile.Save(HandoffPath(session.ProcessId), session.ProcessStartTimeUtcTicks);
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
                if (request != null) { CompletePending(request, response); FinishStepOnly(request, response, true); }
                ProtectUnexpectedDocuments(app, session, request, response);
                ShutdownSolidWorks(ref app, session);
                response.CompletedUtc = DateTime.UtcNow;
                try { if (request != null) Checkpoint(request, response); }
                catch (Exception checkpointError)
                {
                    response.Success = false;
                    response.Message += " 检查点保存失败 / Checkpoint save failed: " + checkpointError.Message;
                }
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
            if (session.TaskLease != null)
            {
                try { session.TaskLease.ReleaseMutex(); } catch { }
                session.TaskLease.Dispose(); session.TaskLease = null;
            }
            session.NativeShutdownComplete = true;
        }

        private static void ProtectUnexpectedDocuments(ISldWorks app, SolidWorksSessionContext session, WorkerRequest request, WorkerResponse response)
        {
            if (app == null || session == null || !session.OwnsApplication || session.KeepApplicationOpen) return;
            object[] documents = null;
            try
            {
                HashSet<string> expected = new HashSet<string>(InputPaths(request).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
                foreach (ExportResultItem result in response.ExportResults)
                    foreach (string path in new[] { result.SldprtPath, result.AssemblyPath })
                        if (!string.IsNullOrWhiteSpace(path)) expected.Add(Path.GetFullPath(path));
                string stage = string.IsNullOrWhiteSpace(request.StagingRoot) ? string.Empty : Path.GetFullPath(request.StagingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                documents = app.GetDocuments() as object[] ?? new object[0];
                foreach (object value in documents)
                {
                    IModelDoc2 model = value as IModelDoc2;
                    string path = model == null ? string.Empty : model.GetPathName();
                    if (model == null || model.GetSaveFlag() || string.IsNullOrWhiteSpace(path) ||
                        (!expected.Contains(Path.GetFullPath(path)) && (stage.Length == 0 || !Path.GetFullPath(path).StartsWith(stage, StringComparison.OrdinalIgnoreCase))))
                    {
                        session.KeepApplicationOpen = true;
                        response.SolidWorksKeptOpen = true;
                        response.Message += " 检测到未保存或任务之外的文档，已保留 SolidWorks 会话。 / SolidWorks retained because an unsaved or unrelated document is open.";
                        JsonFile.Save(HandoffPath(session.ProcessId), session.ProcessStartTimeUtcTicks);
                        break;
                    }
                }
            }
            catch
            {
                // An unresponsive document inventory is not authority to kill a user's work.
                session.KeepApplicationOpen = true;
                response.SolidWorksKeptOpen = true;
            }
            finally { if (documents != null) foreach (object value in documents) ExportIntegrity.Release(value); }
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
                bool authorizedLaunch = request.AuthorizedSolidWorksProcessId > 0 && !WasHandedOff(request);
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
                session.TaskLease = new Mutex(false, "Local\\MasterMiao.SolidWorks." + processId);
                bool acquired;
                try { acquired = session.TaskLease.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    session.OwnsApplication = false;
                    session.TaskLease.Dispose(); session.TaskLease = null;
                    throw new InvalidOperationException("另一个 Master Miao 任务正在使用此 SolidWorks 会话。 / Another Master Miao task is using this SolidWorks session.");
                }
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

        private static string HandoffPath(int processId)
        {
            return Path.Combine(AppPaths.Jobs, "solidworks-handedoff-" + processId + ".json");
        }

        private static bool WasHandedOff(WorkerRequest request)
        {
            string marker = HandoffPath(request.AuthorizedSolidWorksProcessId);
            return File.Exists(marker) && JsonFile.Load<long>(marker) == request.AuthorizedSolidWorksStartTimeUtcTicks;
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
            // Keep scanning compatible with V1.2.5: one linear pass over each body.
            // Export-time identity checks remain strict, but a scan must not reject a
            // document merely because SolidWorks marked it dirty after opening it.
            List<SourceRecord> scanned = new List<SourceRecord>();
            // Keep completed/failed source records available if a later source is
            // cancelled or interrupted before normal completion.
            response.Sources = scanned;

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
                bool hasUnsavedState = false;
                try
                {
                    FileInfo info = new FileInfo(source.Path);
                    if (!info.Exists) throw new FileNotFoundException("源文件不存在。", source.Path);
                    source.Length = info.Length;
                    source.LastWriteTicks = info.LastWriteTimeUtc.Ticks;
                    source.Path = Path.GetFullPath(source.Path);
                    source.ContentSha256 = ExportIntegrity.FileHash(source.Path);
                    int openErrors = 0, openWarnings = 0;
                    int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
                    Emit("PROGRESS", Percent(fileIndex, request.Sources.Count, 5), "读取文件", source.Name);
                    model = app.GetOpenDocumentByName(source.Path) as IModelDoc2;
                    wasAlreadyOpen = model != null;
                    if (model == null) model = app.OpenDoc6(source.Path, (int)swDocumentTypes_e.swDocPART, options, string.Empty, ref openErrors, ref openWarnings);
                    if (model == null) throw new InvalidOperationException(string.Format("无法打开文件，错误={0}，警告={1}。", openErrors, openWarnings));
                    title = model.GetTitle();
                    try { hasUnsavedState = model.GetSaveFlag(); } catch { }
                    source.Configuration = ExportIntegrity.Configuration(model);
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
                        try
                        {
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
                                SourceSha256 = source.ContentSha256,
                                Configuration = source.Configuration,
                                PersistReference = ExportIntegrity.PersistentReference(model, body),
                                Status = "已读取"
                            };
                            ExportIntegrity.Capture(body, item);
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
                                    if (previewError is SolidWorksInterferenceException || previewError is OperationCanceledException) throw;
                                    item.Status = "预览失败";
                                    item.Message = previewError.Message;
                                }
                            }
                            source.Bodies.Add(item);
                        }
                        finally { Release(body); }
                    }
                    ExportIntegrity.VerifySourceFile(source.Path, source.ContentSha256);
                    source.Status = "读取完成";
                    source.Message = (openWarnings == 0 ? string.Empty : "SolidWorks 打开警告：" + openWarnings) +
                        (hasUnsavedState ? (openWarnings == 0 ? string.Empty : "；") + "检测到未保存或自动重建状态：可以继续查看和分类，正式导出前请保存源文件并重新读取。" : string.Empty);
                }
                catch (Exception fileError)
                {
                    if (fileError is SolidWorksInterferenceException || fileError is OperationCanceledException) throw;
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

        internal static string BuildGeometryKey(IBody2 body)
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
                string label = "疑似重复 " + group.ToString("D2");
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
            if (request.ExportSettings.StepOnly && (!request.ExportSettings.ExportStep || request.ExportSettings.CreateAssembly))
                throw new InvalidDataException("仅 STEP 模式不保留原生装配体。 / STEP-only mode cannot retain a native assembly.");
            if (request.ExportSettings.CreateAssembly && !request.ExportSettings.ExportSldprt) throw new InvalidOperationException("生成装配体时必须同时导出 SLDPRT 零件。");
            bool needAssembly = request.ExportSettings.CreateAssembly || request.ExportSettings.ExportStep;
            if (needAssembly && string.IsNullOrWhiteSpace(response.AssemblyTemplatePath)) throw new InvalidOperationException("没有找到可用的 SolidWorks 装配体模板。");
            if (string.IsNullOrWhiteSpace(response.TemplatePath)) throw new InvalidOperationException("没有找到可用的 SolidWorks 零件模板。");
            Directory.CreateDirectory(request.StagingRoot);
            List<ExportResultItem> results = response.ExportResults;
            if (results.Count == 0) results.AddRange(request.ExportItems.Select(plan => CreateResult(plan, request.ExportSettings)));
            PlanOutputPaths(request, results);
            Checkpoint(request, response);
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
                    string sourceHash = groupPlans[0].SourceSha256;
                    string configuration = groupPlans[0].Configuration;
                    if (groupPlans.Any(plan => plan.SourceSha256 != sourceHash || plan.Configuration != configuration))
                        throw new InvalidDataException("同一源文件的任务身份或配置不一致。 / Inconsistent source identity or configuration in task.");
                    ExportIntegrity.VerifySourceFile(sourceGroup.Key, sourceHash);
                    int openErrors = 0, openWarnings = 0;
                    int openOptions = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
                    sourceModel = app.GetOpenDocumentByName(sourceGroup.Key) as IModelDoc2;
                    sourceWasAlreadyOpen = sourceModel != null;
                    if (sourceModel == null) sourceModel = app.OpenDoc6(sourceGroup.Key, (int)swDocumentTypes_e.swDocPART, openOptions, configuration, ref openErrors, ref openWarnings);
                    if (sourceModel == null) throw new InvalidOperationException("无法重新打开源文件：" + sourceGroup.Key);
                    VerifyOpenedSource(sourceModel, configuration, sourceWasAlreadyOpen);
                    ExportIntegrity.VerifySourceFile(sourceGroup.Key, sourceHash);
                    sourceTitle = sourceModel.GetTitle();
                    sourcePart = (IPartDoc)sourceModel;
                    bodies = sourcePart.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];

                    foreach (ExportPlanItem plan in groupPlans)
                    {
                        CheckCancellation(request.CancelFile);
                        completed++;
                        Emit("PROGRESS", Percent(completed, request.ExportItems.Count, 0), "导出零件", string.Format("{0}/{1}：{2}", completed, request.ExportItems.Count, plan.ExportName));
                        ExportResultItem result = results.Single(item => item.BodyId == plan.BodyId);
                        try
                        {
                            ExportIntegrity.VerifySourceFile(sourceGroup.Key, sourceHash);
                            IBody2 selectedBody = ExportIntegrity.ResolveBody(app, sourceModel, bodies, plan);
                            VerifyDuplicateMembers(app, sourceModel, bodies, selectedBody, plan, request.CancelFile);
                            ExportOne(app, sourceModel, selectedBody, response.TemplatePath, request, plan, result);
                            try { ExportIntegrity.VerifyMemory(sourceModel, configuration); }
                            catch (Exception changed) { throw new SolidWorksInterferenceException(changed.Message); }
                        }
                        catch (Exception itemError)
                        {
                            if (itemError is SolidWorksInterferenceException || itemError is OperationCanceledException) throw;
                            result.Message = itemError.Message;
                            if (request.ExportSettings.ExportSldprt && !IsSuccessful(result.SldprtStatus)) result.SldprtStatus = "失败";
                            if (request.ExportSettings.ExportStep && !IsSuccessful(result.StepStatus) && !result.StepStatus.StartsWith("跳过")) result.StepStatus = "失败";
                        }
                        finally { UpdateOutcome(result); Checkpoint(request, response); }
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
                    if (sourceError is SolidWorksInterferenceException || sourceError is OperationCanceledException) throw;
                    foreach (ExportPlanItem plan in groupPlans)
                    {
                        ExportResultItem result = results.Single(item => item.BodyId == plan.BodyId);
                        if (result.SldprtStatus != "未执行") continue;
                        result.Message = sourceError.Message;
                        result.SldprtStatus = request.ExportSettings.ExportSldprt ? "失败" : "未启用";
                        result.StepStatus = request.ExportSettings.ExportStep ? "失败" : "未启用";
                        UpdateOutcome(result);
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
                    Checkpoint(request, response);
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

        private static void VerifyDuplicateMembers(ISldWorks app, IModelDoc2 sourceModel, object[] sourceBodies, IBody2 representative, ExportPlanItem plan, string cancelFile)
        {
            List<BodyRecord> members = plan.DuplicateMembers ?? new List<BodyRecord>();
            if (plan.Quantity != members.Count + 1)
                throw new InvalidDataException("重复组缺少成员身份，请重新建立导出任务。 / Duplicate member identities are missing; create a new export task.");
            foreach (IGrouping<string, BodyRecord> group in members.GroupBy(body => body.SourcePath, StringComparer.OrdinalIgnoreCase))
            {
                bool sameSource = string.Equals(group.Key, plan.SourcePath, StringComparison.OrdinalIgnoreCase);
                IModelDoc2 model = sameSource ? sourceModel : null;
                object[] bodies = sameSource ? sourceBodies : null;
                bool alreadyOpen = true;
                try
                {
                    CheckCancellation(cancelFile);
                    BodyRecord first = group.First();
                    if (group.Any(body => body.SourceSha256 != first.SourceSha256 || body.Configuration != first.Configuration))
                        throw new InvalidDataException("重复组的源文件身份或配置不一致。 / Duplicate source identity or configuration is inconsistent.");
                    ExportIntegrity.VerifySourceFile(group.Key, first.SourceSha256);
                    if (!sameSource)
                    {
                        model = app.GetOpenDocumentByName(group.Key) as IModelDoc2;
                        alreadyOpen = model != null;
                        int errors = 0, warnings = 0;
                        if (model == null) model = app.OpenDoc6(group.Key, (int)swDocumentTypes_e.swDocPART,
                            (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly, first.Configuration, ref errors, ref warnings);
                        if (model == null) throw new InvalidDataException("无法打开重复成员源文件： / Cannot open duplicate source: " + group.Key);
                        VerifyOpenedSource(model, first.Configuration, alreadyOpen);
                        bodies = ((IPartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];
                    }
                    foreach (BodyRecord member in group)
                    {
                        CheckCancellation(cancelFile);
                        IBody2 actual = ExportIntegrity.ResolveBody(app, model, bodies, ExportIntegrity.BodyIdentity(member));
                        try { ExportIntegrity.VerifySameShape(representative, actual); }
                        catch (InvalidDataException ex) { throw new InvalidDataException(member.SourceName + " / " + member.OriginalName + ": " + ex.Message, ex); }
                    }
                    ExportIntegrity.VerifyMemory(model, first.Configuration);
                    ExportIntegrity.VerifySourceFile(group.Key, first.SourceSha256);
                }
                finally
                {
                    if (!sameSource)
                    {
                        if (bodies != null) foreach (object body in bodies) Release(body);
                        if (model != null && !alreadyOpen) { try { app.CloseDoc(model.GetTitle()); } catch { } }
                        Release(model);
                    }
                }
            }
            ExportIntegrity.VerifyMemory(sourceModel, plan.Configuration);
        }

        internal static bool IsSuccessful(string status)
        {
            return status == "成功" || status == "沿用（已验证）";
        }

        private static void VerifyOpenedSource(IModelDoc2 model, string configuration, bool alreadyOpen)
        {
            if (!alreadyOpen && model.GetSaveFlag())
                throw new InvalidDataException("文件打开后被 SolidWorks 标记为需要保存，可能发生了自动重建或版本转换。为避免使用与磁盘不一致的几何，请先在 SolidWorks 中确认保存，或使用独立副本；程序不会保存源文件。 / SolidWorks marked this newly opened file as needing a save, possibly after automatic rebuild or version conversion. Confirm and save it in SolidWorks, or use a separate copy, before scanning. Master Miao does not save the source.");
            ExportIntegrity.VerifyMemory(model, configuration);
        }

        private static ExportResultItem CreateResult(ExportPlanItem plan, ExportSettings settings)
        {
            return new ExportResultItem
            {
                BodyId = plan.BodyId,
                SourcePath = plan.SourcePath,
                SourceName = plan.SourceName,
                OriginalName = plan.OriginalName,
                ExportName = plan.ExportName,
                PlannedExportName = plan.ExportName,
                CategoryPath = plan.CategoryPath,
                PreviewFront = plan.PreviewFront,
                PreviewTop = plan.PreviewTop,
                PreviewIso = plan.PreviewIso,
                Quantity = plan.Quantity,
                Occurrences = new List<string>(plan.Occurrences ?? new List<string>()),
                SldprtStatus = settings.ExportSldprt ? "未执行" : "未启用",
                StepStatus = settings.ExportStep ? "未执行" : "未启用",
                AssemblyStatus = settings.CreateAssembly ? "未执行" : "未启用",
                Outcome = "未执行",
                ExpectedVolume = plan.Volume,
                ExpectedArea = plan.SurfaceArea,
                ExpectedBounds = plan.GeometryBounds,
                ExpectedGeometryEvidenceKey = plan.GeometryEvidenceKey
            };
        }

        private static void ExportOne(ISldWorks app, IModelDoc2 sourceModel, IBody2 body, string template, WorkerRequest request, ExportPlanItem plan, ExportResultItem result)
        {
            IBody2 copy = null;
            IModelDoc2 target = null;
            IPartDoc targetPart = null;
            IModelDocExtension extension = null;
            object feature = null;
            string targetTitle = string.Empty;
            string finalSldprt = result.SldprtPath;
            string finalStep = result.StepPath;
            string outputFolder = Path.GetDirectoryName(finalSldprt);
            string stepFolder = request.ExportSettings.ExportStep ? Path.GetDirectoryName(finalStep) : string.Empty;
            Directory.CreateDirectory(outputFolder);
            if (request.ExportSettings.ExportStep) Directory.CreateDirectory(stepFolder);
            bool existingSldprt = File.Exists(finalSldprt);
            bool existingStep = File.Exists(finalStep);
            if (request.ExportSettings.ExportSldprt)
            {
                result.SldprtPath = finalSldprt;
                if (existingSldprt && request.ExportSettings.ConflictPolicy == "跳过") result.SldprtStatus = "跳过（未验证）";
            }
            if (request.ExportSettings.ExportStep)
            {
                result.StepPath = finalStep;
                result.StepStatus = existingStep && request.ExportSettings.ConflictPolicy == "跳过" ? "跳过（未验证）" : "待批量导出";
            }
            if (existingSldprt && request.ExportSettings.ConflictPolicy == "跳过")
            {
                result.VerificationStatus = result.SldprtVerification = "跳过未验证";
                if (request.ExportSettings.ExportStep && !existingStep)
                {
                    result.StepStatus = "失败";
                    result.Message = "已有零件未验证，不作为新 STEP 或装配体的输入；请使用自动编号或覆盖重新生成。 / Unverified existing part cannot be input to a new STEP or assembly; regenerate.";
                }
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

                CheckCancellation(request.CancelFile);
                VerifySingleBody(app, stageSldprt, body);
                result.VerificationStatus = result.SldprtVerification = "几何验证通过";
                ExportIntegrity.VerifySourceFile(plan.SourcePath, plan.SourceSha256);
                ExportIntegrity.VerifyMemory(sourceModel, plan.Configuration);
                if (!ExportIntegrity.EvidenceMatches(body, plan))
                    throw new SolidWorksInterferenceException("提交前源实体发生变化，本次文件未提交。 / Source body changed before commit; artifact not committed.");
                CheckCancellation(request.CancelFile);
                if (request.ExportSettings.ExportSldprt)
                {
                    result.SldprtPath = finalSldprt;
                    ExportIntegrity.CommitFile(stageSldprt, result.SldprtPath, request.ExportSettings.ConflictPolicy == "覆盖", InputPaths(request));
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
            string finalPath = keepAssembly ? sourceResults[0].AssemblyPath : string.Empty;
            result.AssemblyPath = finalPath;
            result.AssemblyStepPath = sourceResults[0].AssemblyStepPath;
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
                result.Status = "跳过（未验证）";
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
                        result.Status = "跳过（未验证）";
                        result.Message = "目标装配体已存在；STEP 将使用本次生成的临时装配体。";
                    }
                    else
                    {
                        CheckCancellation(request.CancelFile);
                        ExportIntegrity.CommitFile(stagePath, finalPath, request.ExportSettings.ConflictPolicy == "覆盖", InputPaths(request));
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
                if (ex is OperationCanceledException || ex is SolidWorksInterferenceException) throw;
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
                    IMathTransform transform = component.Transform2;
                    try
                    {
                        if (transform == null || !ExportIntegrity.IdentityTransform(transform.ArrayData as double[]))
                            throw new InvalidDataException("原位装配体组件的位置或方向不一致。 / In-place component transform is not identity.");
                    }
                    finally { Release(transform); }
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

        private static void VerifySingleBody(ISldWorks app, string path, IBody2 expected)
        {
            IModelDoc2 model = null;
            IPartDoc part = null;
            string title = string.Empty;
            object[] bodies = null;
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
                bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null || bodies.Length != 1) throw new InvalidOperationException("导出文件没有通过单实体验证。");
                ExportIntegrity.VerifyGeometry(expected, (IBody2)bodies[0]);
            }
            finally
            {
                if (bodies != null) foreach (object body in bodies) Release(body);
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

        internal static IEnumerable<string> InputPaths(WorkerRequest request)
        {
            return (request.Sources ?? new List<SourceRecord>()).Select(item => item.Path)
                .Concat((request.ExportItems ?? new List<ExportPlanItem>()).Select(item => item.SourcePath))
                .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        internal static void PlanOutputPaths(WorkerRequest request, List<ExportResultItem> results)
        {
            HashSet<string> reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> reservedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool globallyUnique = request.ExportSettings.ExportStep || request.ExportSettings.CreateAssembly;
            bool numbered = request.ExportSettings.ConflictPolicy == "自动编号";
            foreach (ExportResultItem result in results)
            {
                string relative = SafeRelativeFolder(result.CategoryPath);
                string partFolder = Path.Combine(GetPartOutputRoot(request), relative);
                string stepFolder = Path.Combine(GetStepOutputRoot(request), relative);
                string stem = NameRules.SafeStem(result.PlannedExportName, "零件");
                string candidate = stem;
                for (int suffix = 1; ; suffix++)
                {
                    candidate = suffix == 1 ? stem : stem + "_" + suffix;
                    string partPath = Path.GetFullPath(Path.Combine(partFolder, candidate + ".SLDPRT"));
                    string stepPath = Path.GetFullPath(Path.Combine(stepFolder, candidate + ".STEP"));
                    bool taskCollision = reservedPaths.Contains(partPath) || (request.ExportSettings.ExportStep && reservedPaths.Contains(stepPath)) || (globallyUnique && reservedStems.Contains(candidate));
                    bool diskCollision = ExistsForSelectedFormats(partFolder, stepFolder, candidate, request.ExportSettings);
                    if (taskCollision && !numbered) throw new InvalidDataException("任务中的最终文件名冲突，请调整名称或使用自动编号： / Final task filename collision: " + candidate);
                    if (taskCollision || (numbered && diskCollision))
                    {
                        if (suffix >= 9999) throw new IOException("无法规划唯一输出名称。 / Unable to allocate unique output name.");
                        continue;
                    }
                    ExportIntegrity.EnsureNotSource(partPath, InputPaths(request));
                    ExportIntegrity.EnsureNotSource(stepPath, InputPaths(request));
                    result.ExportName = candidate;
                    result.SldprtPath = partPath;
                    result.StepPath = request.ExportSettings.ExportStep ? stepPath : string.Empty;
                    reservedPaths.Add(partPath);
                    if (request.ExportSettings.ExportStep) reservedPaths.Add(stepPath);
                    reservedStems.Add(candidate);
                    break;
                }
            }
            if (!globallyUnique) return;
            foreach (IGrouping<string, ExportResultItem> group in results.GroupBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase))
            {
                string stem = NameRules.SafeStem(Path.GetFileNameWithoutExtension(group.Key), "多实体零件") + "_拆分装配体";
                for (int suffix = 1; ; suffix++)
                {
                    string candidate = suffix == 1 ? stem : stem + "_" + suffix;
                    string assembly = Path.GetFullPath(Path.Combine(GetPartOutputRoot(request), candidate + ".SLDASM"));
                    string step = Path.GetFullPath(Path.Combine(GetStepOutputRoot(request), candidate + ".STEP"));
                    bool taskCollision = reservedStems.Contains(candidate) || reservedPaths.Contains(assembly) || reservedPaths.Contains(step);
                    bool diskCollision = (request.ExportSettings.CreateAssembly && File.Exists(assembly)) || (request.ExportSettings.ExportStep && File.Exists(step));
                    if (taskCollision && !numbered) throw new InvalidDataException("装配体名称冲突： / Assembly name collision: " + candidate);
                    if (taskCollision || (numbered && diskCollision))
                    {
                        if (suffix >= 9999) throw new IOException("无法规划唯一装配体名称。 / Unable to allocate assembly name.");
                        continue;
                    }
                    ExportIntegrity.EnsureNotSource(assembly, InputPaths(request));
                    ExportIntegrity.EnsureNotSource(step, InputPaths(request));
                    foreach (ExportResultItem item in group)
                    {
                        item.AssemblyPath = request.ExportSettings.CreateAssembly ? assembly : string.Empty;
                        item.AssemblyStepPath = request.ExportSettings.ExportStep ? step : string.Empty;
                    }
                    reservedPaths.Add(assembly); reservedPaths.Add(step); reservedStems.Add(candidate);
                    break;
                }
            }
        }

        internal static void UpdateOutcome(ExportResultItem item)
        {
            string[] statuses = { item.SldprtStatus, item.StepStatus, item.AssemblyStatus, item.AssemblyStepStatus };
            item.Outcome = statuses.Any(s => s == "失败") ? "失败" :
                statuses.Any(s => s == "取消") ? "取消" :
                statuses.Any(s => s != null && s.StartsWith("跳过", StringComparison.Ordinal)) ? "跳过未验证" :
                statuses.Any(s => s == "未执行" || s == "待批量导出" || s == "已生成") ? "未执行" :
                statuses.Any(s => s == "成功") ? "本次成功" : "未执行";
        }

        internal static void Checkpoint(WorkerRequest request, WorkerResponse response)
        {
            if (string.IsNullOrWhiteSpace(request.CheckpointPath)) return;
            foreach (ExportResultItem item in response.ExportResults) UpdateOutcome(item);
            JsonFile.Save(request.CheckpointPath, response);
        }

        private static void CompletePending(WorkerRequest request, WorkerResponse response)
        {
            foreach (ExportResultItem item in response.ExportResults)
            {
                if (response.Cancelled)
                {
                    if (item.SldprtStatus == "已生成") item.SldprtStatus = "取消";
                    if (item.StepStatus == "待批量导出") item.StepStatus = "取消";
                    if (item.AssemblyStatus == "未执行" && item.SldprtStatus == "成功") item.AssemblyStatus = "取消";
                }
                else if (!response.Success)
                {
                    if (item.SldprtStatus == "已生成") item.SldprtStatus = "失败";
                    if (item.StepStatus == "待批量导出") item.StepStatus = "失败";
                }
                if ((item.SldprtStatus == "失败" || item.StepStatus == "失败" || item.SldprtStatus == "取消" || item.StepStatus == "取消") && string.IsNullOrWhiteSpace(item.Message)) item.Message = response.Message;
                UpdateOutcome(item);
            }
        }

        internal static string GetPartOutputRoot(WorkerRequest request)
        {
            if (request.ExportSettings != null && request.ExportSettings.StepOnly)
            {
                if (string.IsNullOrWhiteSpace(request.StagingRoot) || !Path.IsPathRooted(request.StagingRoot))
                    throw new InvalidDataException("仅 STEP 模式需要独立的任务暂存目录。 / STEP-only mode requires an absolute task staging directory.");
                return Path.Combine(request.StagingRoot, "step-only-parts");
            }
            return request.ExportSettings != null && request.ExportSettings.SeparateStepOutput
                ? Path.Combine(request.OutputRoot, "零件源文件")
                : request.OutputRoot;
        }

        internal static string GetStepOutputRoot(WorkerRequest request)
        {
            if (request.ExportSettings != null && request.ExportSettings.StepOnly) return request.OutputRoot;
            return request.ExportSettings != null && request.ExportSettings.SeparateStepOutput
                ? Path.Combine(request.OutputRoot, "STEP生产文件")
                : request.OutputRoot;
        }

        private static bool ExistsForSelectedFormats(string partFolder, string stepFolder, string stem, ExportSettings settings)
        {
            return (settings.ExportSldprt && File.Exists(Path.Combine(partFolder, stem + ".SLDPRT")))
                || (settings.ExportStep && File.Exists(Path.Combine(stepFolder, stem + ".STEP")));
        }

        internal static void FinishStepOnly(WorkerRequest request, WorkerResponse response, bool cleanup)
        {
            if (request.ExportSettings == null || !request.ExportSettings.StepOnly) return;
            string temporaryRoot;
            try { temporaryRoot = Path.GetFullPath(GetPartOutputRoot(request)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; }
            catch { temporaryRoot = string.Empty; }
            foreach (ExportResultItem item in response.ExportResults)
            {
                string path = item.SldprtPath;
                if (cleanup && !string.IsNullOrWhiteSpace(path) && temporaryRoot.Length > 0)
                {
                    try
                    {
                        string absolute = Path.GetFullPath(path);
                        if (absolute.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(Path.GetExtension(absolute), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                        {
                            ExportIntegrity.EnsureNotSource(absolute, InputPaths(request));
                            if (File.Exists(absolute)) File.Delete(absolute);
                        }
                    }
                    catch (Exception ex) { item.Message += "\n中间文件暂未清理 / Intermediate file retained: " + ex.Message; }
                }
                if (item.SldprtStatus == "失败" && !IsSuccessful(item.StepStatus)) item.StepStatus = "失败";
                // A task prerequisite is not a delivered file and must not inflate reports.
                item.SldprtPath = string.Empty; item.SldprtStatus = "未启用"; item.SldprtVerification = string.Empty;
                UpdateOutcome(item);
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
