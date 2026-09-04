using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace SWBodyOrganizer
{
    internal static class AssemblyStepExporter
    {
        private sealed class StepJob
        {
            public int Index;
            public AssemblyResultItem Assembly;
            public List<ExportResultItem> Parts;
            public string StageFolder;
            public string StageAssemblyStep;
        }

        public static bool TryFindSolidWorksExecutable(out string executable)
        {
            executable = string.Empty;
            string[] known =
            {
                @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe",
                @"C:\Program Files (x86)\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe"
            };
            executable = known.FirstOrDefault(File.Exists) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(executable)) return true;

            try
            {
                using (RegistryKey progId = Registry.ClassesRoot.OpenSubKey(@"SldWorks.Application\CLSID"))
                {
                    string clsid = progId == null ? string.Empty : Convert.ToString(progId.GetValue(null));
                    if (!string.IsNullOrWhiteSpace(clsid))
                    {
                        using (RegistryKey server = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + @"\LocalServer32"))
                        {
                            string command = server == null ? string.Empty : Convert.ToString(server.GetValue(null));
                            executable = ExtractExecutable(command);
                        }
                    }
                }
            }
            catch { executable = string.Empty; }
            return !string.IsNullOrWhiteSpace(executable) && File.Exists(executable);
        }

        public static void Export(WorkerRequest request, WorkerResponse response, SolidWorksSessionContext session, ISldWorks app)
        {
            List<ExportResultItem> allParts = response.ExportResults ?? new List<ExportResultItem>();
            List<AssemblyResultItem> assemblies = response.AssemblyResults ?? new List<AssemblyResultItem>();
            List<StepJob> jobs = new List<StepJob>();
            foreach (AssemblyResultItem assembly in assemblies)
            {
                List<ExportResultItem> parts = allParts.Where(item => string.Equals(item.SourcePath, assembly.SourcePath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!WorkerMain.IsSuccessful(assembly.Status) || string.IsNullOrWhiteSpace(assembly.StepSourceAssemblyPath) || !File.Exists(assembly.StepSourceAssemblyPath))
                {
                    assembly.StepStatus = "失败";
                    MarkPendingPartsFailed(parts, "用于批量 STEP 导出的装配体不可用：" + assembly.Message);
                    continue;
                }

                StepJob job = new StepJob
                {
                    Index = jobs.Count,
                    Assembly = assembly,
                    Parts = parts,
                    StageFolder = Path.Combine(request.StagingRoot, "assembly_step_" + jobs.Count.ToString("D3", CultureInfo.InvariantCulture))
                };
                Directory.CreateDirectory(job.StageFolder);
                job.StageAssemblyStep = Path.Combine(job.StageFolder, "__SWBO_ASSEMBLY_" + Guid.NewGuid().ToString("N") + ".STEP");
                jobs.Add(job);
            }

            if (jobs.Count == 0)
            {
                FinalizeResponse(request, response);
                return;
            }

            Process[] activeProcesses = Process.GetProcessesByName("SLDWORKS");
            try
            {
                if (!activeProcesses.Any(item => item.Id == session.ProcessId))
                    throw new SolidWorksInterferenceException((session.WasRunning ? "用户原有的" : "程序启动的") + " SolidWorks 会话在 STEP 导出前已经关闭或发生变化，本次任务已停止。");
                if (activeProcesses.Any(item => item.Id != session.ProcessId))
                    throw new SolidWorksInterferenceException("STEP 导出前检测到另一个 SolidWorks 窗口。为避免连接到错误会话，本次任务已停止。");
            }
            finally
            {
                foreach (Process activeProcess in activeProcesses) activeProcess.Dispose();
            }

            string macroPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MasterMiao.StepMacro.dll");
            string logPath = Path.Combine(request.StagingRoot, "SWBO_AssemblyStepExport.log");
            string jobPath = Path.Combine(Path.GetTempPath(), "SWBodyOrganizer.StepJob." + session.ProcessId.ToString(CultureInfo.InvariantCulture) + ".txt");
            if (!File.Exists(macroPath))
            {
                MarkJobsFailed(jobs, "发行包缺少自动 STEP 宏组件：" + macroPath);
                FinalizeResponse(request, response);
                return;
            }
            WriteMacroJob(jobPath, logPath, session.OriginalActiveTitle, jobs);

            try
            {
                if (session.WasRunning)
                    WorkerMain.Emit("PROGRESS", 74, "导出 STEP", "正在复用并保护用户已打开的 SolidWorks 窗口");
                else
                    WorkerMain.Emit("PROGRESS", 74, "导出 STEP", "正在显示程序启动的 SolidWorks 批量导出窗口");

                try { app.CommandInProgress = false; } catch { }
                try { app.Visible = true; } catch { }
                if (!session.WasRunning) try { app.UserControl = true; } catch { }
                int macroError;
                bool macroStarted = false;
                try { macroStarted = app.RunMacro2(macroPath, string.Empty, "Main", 0, out macroError); }
                catch { macroError = -1; }
                if (!macroStarted)
                    throw new InvalidOperationException("SolidWorks 未能自动启动批量 STEP 宏，错误=" + macroError.ToString(CultureInfo.InvariantCulture) + "。本程序不会要求用户手动运行宏。");

                int expectedFiles = jobs.Sum(item => item.Parts.Count + 1);
                int lastCount = -1;
                DateTime lastProgress = DateTime.MinValue;
                Stopwatch timer = Stopwatch.StartNew();
                TimeSpan timeout = TimeSpan.FromMinutes(Math.Max(15.0, Math.Min(120.0, expectedFiles / 5.0)));
                while (!HasCompletedLog(logPath))
                {
                    WorkerMain.CheckCancellation(request.CancelFile);
                    int count = jobs.Sum(item => Directory.GetFiles(item.StageFolder, "*.STEP", SearchOption.TopDirectoryOnly).Length);
                    if (count != lastCount || DateTime.UtcNow - lastProgress > TimeSpan.FromSeconds(5))
                    {
                        int percent = 74 + Math.Min(22, expectedFiles == 0 ? 0 : (int)Math.Round((double)count / expectedFiles * 22.0));
                        WorkerMain.Emit("PROGRESS", percent, "导出 STEP", string.Format("已生成 {0}/{1} 个 STEP 文件", count, expectedFiles));
                        lastCount = count;
                        lastProgress = DateTime.UtcNow;
                    }
                    if (!IsProcessAlive(session.ProcessId))
                        throw new SolidWorksInterferenceException((session.WasRunning ? "用户原有的" : "程序启动的") + " SolidWorks 会话在 STEP 导出期间被关闭，本次任务已停止。");
                    if (timer.Elapsed > timeout)
                        throw new TimeoutException("SolidWorks 装配体 STEP 导出超时；暂存文件未覆盖正式输出。");
                    Thread.Sleep(500);
                }

                string log = File.ReadAllText(logPath, Encoding.UTF8);
                ValidateMacroLog(log, jobs.Count);
                WorkerMain.Emit("PROGRESS", 97, "归类 STEP", "正在校验并移动到零件分类文件夹");
                foreach (StepJob job in jobs) CommitJob(request, job);
            }
            catch (SolidWorksInterferenceException ex)
            {
                MarkJobsFailed(jobs, ex.Message);
                throw;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                MarkJobsFailed(jobs, ex.Message);
            }
            finally
            {
                TryDelete(jobPath);
                foreach (StepJob job in jobs)
                    if (job.Assembly.Temporary || IsInside(request.StagingRoot, job.Assembly.StepSourceAssemblyPath))
                        TryDelete(job.Assembly.StepSourceAssemblyPath);
            }
            FinalizeResponse(request, response);
        }

        private static void CommitJob(WorkerRequest request, StepJob job)
        {
            if (!IsValidStep(job.StageAssemblyStep))
                throw new InvalidDataException("装配体 STEP 没有通过格式校验：" + Path.GetFileName(job.StageAssemblyStep));

            foreach (ExportResultItem part in job.Parts)
            {
                if (part.StepStatus.StartsWith("跳过", StringComparison.Ordinal) && File.Exists(part.StepPath)) continue;
                string stagePart = Path.Combine(job.StageFolder, Path.GetFileNameWithoutExtension(part.SldprtPath) + ".STEP");
                if (!IsValidStep(stagePart))
                {
                    part.StepStatus = "失败";
                    AppendMessage(part, "装配体批量导出后没有找到有效 STEP：" + Path.GetFileName(stagePart));
                    continue;
                }
                try
                {
                    if (File.Exists(part.StepPath))
                    {
                        if (request.ExportSettings.ConflictPolicy == "跳过")
                        {
                            part.StepStatus = "跳过（已存在）";
                            continue;
                        }
                        BackupExisting(part.StepPath);
                    }
                    File.Copy(stagePart, part.StepPath, false);
                    if (!IsValidStep(part.StepPath)) throw new InvalidDataException("复制后的 STEP 未通过格式校验。");
                    part.StepStatus = "成功";
                }
                catch (Exception ex)
                {
                    part.StepStatus = "失败";
                    AppendMessage(part, ex.Message);
                }
            }

            string finalAssemblyStep = ResolveAssemblyStepPath(request, job.Assembly);
            job.Assembly.AssemblyStepPath = finalAssemblyStep;
            try
            {
                if (File.Exists(finalAssemblyStep))
                {
                    if (request.ExportSettings.ConflictPolicy == "跳过")
                    {
                        job.Assembly.StepStatus = "跳过（已存在）";
                    }
                    else
                    {
                        BackupExisting(finalAssemblyStep);
                        File.Copy(job.StageAssemblyStep, finalAssemblyStep, false);
                        job.Assembly.StepStatus = "成功";
                    }
                }
                else
                {
                    File.Copy(job.StageAssemblyStep, finalAssemblyStep, false);
                    job.Assembly.StepStatus = "成功";
                }
                if (!IsValidStep(finalAssemblyStep)) throw new InvalidDataException("正式装配体 STEP 未通过格式校验。");
            }
            catch (Exception ex)
            {
                job.Assembly.StepStatus = "失败";
                job.Assembly.Message = AppendText(job.Assembly.Message, ex.Message);
            }
            foreach (ExportResultItem part in job.Parts) part.AssemblyStepPath = job.Assembly.AssemblyStepPath;
        }

        private static void WriteMacroJob(string jobPath, string logPath, string originalActiveTitle, IEnumerable<StepJob> jobs)
        {
            List<string> lines = new List<string>
            {
                ToBase64(logPath),
                ToBase64(originalActiveTitle)
            };
            lines.AddRange(jobs.Select(job => ToBase64(job.Assembly.StepSourceAssemblyPath) + "\t" + ToBase64(job.StageAssemblyStep)));
            File.WriteAllLines(jobPath, lines.ToArray(), Encoding.UTF8);
        }

        private static void ValidateMacroLog(string log, int expectedJobs)
        {
            string[] lines = log.Replace("\r", string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (!lines.Any(line => line == "RESTORED|True")) throw new InvalidOperationException("SolidWorks STEP 选项没有确认恢复，正式文件尚未归位。");
            if (!lines.Any(line => line == "DONE")) throw new InvalidOperationException("SolidWorks STEP 宏没有正常完成。");
            string fatal = lines.FirstOrDefault(line => line.StartsWith("FATAL|", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(fatal)) throw new InvalidOperationException("SolidWorks STEP 宏异常：" + fatal);
            for (int index = 0; index < expectedJobs; index++)
            {
                string prefix = "ITEM|" + index.ToString(CultureInfo.InvariantCulture) + "|";
                string line = lines.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
                if (!string.IsNullOrWhiteSpace(line) && line.IndexOf("|INTERFERENCE|", StringComparison.Ordinal) >= 0)
                    throw new SolidWorksInterferenceException("检测到 STEP 导出期间 SolidWorks 活动文档发生变化。为保护正式输出，本次任务已停止。");
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(prefix + "OK|0|0", StringComparison.Ordinal))
                    throw new InvalidOperationException("装配体 STEP 导出失败：" + (line ?? (prefix + "NO_RESULT")));
            }
        }

        private static bool HasCompletedLog(string path)
        {
            if (!File.Exists(path)) return false;
            try { return File.ReadAllLines(path).Any(line => line.Trim() == "DONE"); }
            catch { return false; }
        }

        private static bool IsValidStep(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length == 0) return false;
            try
            {
                using (StreamReader reader = new StreamReader(path, Encoding.ASCII, true))
                {
                    char[] buffer = new char[64];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    return new string(buffer, 0, read).TrimStart().StartsWith("ISO-10303-21;", StringComparison.Ordinal);
                }
            }
            catch { return false; }
        }

        private static string ResolveAssemblyStepPath(WorkerRequest request, AssemblyResultItem assembly)
        {
            string stem = !string.IsNullOrWhiteSpace(assembly.AssemblyPath)
                ? Path.GetFileNameWithoutExtension(assembly.AssemblyPath)
                : NameRules.SafeStem(Path.GetFileNameWithoutExtension(assembly.SourcePath), "多实体零件") + "_拆分装配体";
            string outputRoot = WorkerMain.GetStepOutputRoot(request);
            Directory.CreateDirectory(outputRoot);
            string candidate = Path.Combine(outputRoot, stem + ".STEP");
            if (request.ExportSettings.ConflictPolicy != "自动编号" || !File.Exists(candidate)) return candidate;
            for (int index = 2; index < 10000; index++)
            {
                candidate = Path.Combine(outputRoot, stem + "_" + index.ToString(CultureInfo.InvariantCulture) + ".STEP");
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException("无法为装配体 STEP 生成可用名称。");
        }

        private static void BackupExisting(string path)
        {
            string folder = Path.Combine(AppPaths.Backups, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            File.Move(path, Path.Combine(folder, Path.GetFileName(path)));
        }

        private static void FinalizeResponse(WorkerRequest request, WorkerResponse response)
        {
            List<ExportResultItem> parts = response.ExportResults ?? new List<ExportResultItem>();
            List<AssemblyResultItem> assemblies = response.AssemblyResults ?? new List<AssemblyResultItem>();
            int successfulParts = parts.Count(item => (!request.ExportSettings.ExportSldprt || WorkerMain.IsSuccessful(item.SldprtStatus)) && (!request.ExportSettings.ExportStep || WorkerMain.IsSuccessful(item.StepStatus)));
            int successfulAssemblies = assemblies.Count(item => WorkerMain.IsSuccessful(item.Status));
            int successfulAssemblySteps = assemblies.Count(item => WorkerMain.IsSuccessful(item.StepStatus));
            response.Success = successfulParts == parts.Count
                && (!request.ExportSettings.CreateAssembly || successfulAssemblies == assemblies.Count)
                && (!request.ExportSettings.ExportStep || successfulAssemblySteps == assemblies.Count);
            response.Message = string.Format("导出完成：零件 {0}/{1} 项成功；STEP 装配批次 {2}/{3} 个成功{4}。",
                successfulParts, parts.Count, successfulAssemblySteps, assemblies.Count,
                request.ExportSettings.CreateAssembly ? string.Format("；装配体 {0}/{1} 个成功", successfulAssemblies, assemblies.Count) : string.Empty);
            WorkerMain.Emit("PROGRESS", 100, "完成", response.Message);
        }

        private static void MarkJobsFailed(IEnumerable<StepJob> jobs, string message)
        {
            foreach (StepJob job in jobs)
            {
                job.Assembly.StepStatus = "失败";
                job.Assembly.Message = AppendText(job.Assembly.Message, message);
                MarkPendingPartsFailed(job.Parts, message);
            }
        }

        private static void MarkPendingPartsFailed(IEnumerable<ExportResultItem> parts, string message)
        {
            foreach (ExportResultItem part in parts.Where(item => !WorkerMain.IsSuccessful(item.StepStatus)))
            {
                part.StepStatus = "失败";
                AppendMessage(part, message);
            }
        }

        private static void AppendMessage(ExportResultItem item, string message)
        {
            item.Message = AppendText(item.Message, message);
        }

        private static string AppendText(string existing, string message)
        {
            if (string.IsNullOrWhiteSpace(existing)) return message ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message) || existing.IndexOf(message, StringComparison.Ordinal) >= 0) return existing;
            return existing + "；" + message;
        }

        private static string ToBase64(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string ExtractExecutable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            command = command.Trim();
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : string.Empty;
            }
            int space = command.IndexOf(' ');
            return space > 0 ? command.Substring(0, space) : command;
        }

        private static bool IsProcessAlive(int processId)
        {
            if (processId <= 0) return false;
            try { using (Process process = Process.GetProcessById(processId)) return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }

        private static bool IsInside(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return false;
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
