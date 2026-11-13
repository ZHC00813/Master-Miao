using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Blankedge.SWHoleTools.BatchExporter.Model;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    public sealed class SolidWorksSession : IDisposable
    {
        private readonly bool _ownsApplication;
        private readonly Process _ownedProcess;
        private bool _disposed;

        private SolidWorksSession(ISldWorks application, bool ownsApplication, Process ownedProcess = null)
        {
            Application = application;
            _ownsApplication = ownsApplication;
            _ownedProcess = ownedProcess;
        }

        public ISldWorks Application { get; private set; }

        public static SolidWorksSession Connect(SolidWorksConnectionMode mode, CancellationToken cancellationToken = default(CancellationToken))
        {
            ISldWorks running = TryGetRunning();
            if (mode == SolidWorksConnectionMode.ExistingOnly)
            {
                if (running == null)
                {
                    throw new InvalidOperationException("未检测到正在运行的 SOLIDWORKS。请先启动 SOLIDWORKS，或显式选择“启动独立隐藏会话”。");
                }
                return new SolidWorksSession(running, false);
            }

            if (running != null)
            {
                throw new InvalidOperationException("检测到现有 SOLIDWORKS 会话。为避免误关用户会话，隐藏会话模式已停止；请改用“连接当前会话”或先关闭现有会话。");
            }

            return StartOwned(cancellationToken);
        }

        private static SolidWorksSession StartOwned(CancellationToken cancellationToken)
        {
            string executable = ResolveLocalServerPath();
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null)
            {
                throw new InvalidOperationException("无法启动 SOLIDWORKS 进程。");
            }

            int timeoutSeconds = ReadStartupTimeoutSeconds();
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                while (timer.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    process.Refresh();
                    if (process.HasExited)
                    {
                        throw new InvalidOperationException("SOLIDWORKS 在注册 COM 服务前退出，退出码：" + process.ExitCode);
                    }

                    ISldWorks application = TryGetRunning();
                    if (application != null)
                    {
                        application.UserControl = false;
                        application.Visible = false;
                        return new SolidWorksSession(application, true, process);
                    }
                    Thread.Sleep(500);
                }

                throw new TimeoutException("SOLIDWORKS 在 " + timeoutSeconds + " 秒内未完成启动。请先手动启动并处理许可/首次运行提示，再选择“连接当前会话”。");
            }
            catch
            {
                StopOwnedProcess(process);
                process.Dispose();
                throw;
            }
        }

        private static ISldWorks TryGetRunning()
        {
            try
            {
                return Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
            }
            catch (COMException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string ResolveLocalServerPath()
        {
            Type type = Type.GetTypeFromProgID("SldWorks.Application", true);
            string keyPath = @"CLSID\" + type.GUID.ToString("B") + @"\LocalServer32";
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(keyPath, false))
            {
                string raw = key?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(raw))
                    throw new InvalidOperationException("无法从 COM 注册表解析 SOLIDWORKS 路径。");
                raw = System.Environment.ExpandEnvironmentVariables(raw.Trim());
                string path;
                if (raw.StartsWith("\"", StringComparison.Ordinal))
                {
                    int closing = raw.IndexOf('"', 1);
                    path = closing > 1 ? raw.Substring(1, closing - 1) : raw.Trim('"');
                }
                else
                {
                    int ending = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    path = ending >= 0 ? raw.Substring(0, ending + 4) : raw;
                }
                if (!File.Exists(path)) throw new FileNotFoundException("SOLIDWORKS COM 服务器不存在。", path);
                return path;
            }
        }

        private static int ReadStartupTimeoutSeconds()
        {
            const int defaultSeconds = 90;
            string value = System.Environment.GetEnvironmentVariable("BLANKEDGE_SW_START_TIMEOUT_SECONDS");
            return int.TryParse(value, out int seconds) ? Math.Max(10, Math.Min(300, seconds)) : defaultSeconds;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_ownsApplication)
                {
                    try { Application?.ExitApp(); } catch { }
                }
            }
            finally
            {
                if (Application != null && Marshal.IsComObject(Application))
                {
                    Marshal.FinalReleaseComObject(Application);
                }
                Application = null;
                if (_ownedProcess != null)
                {
                    try
                    {
                        if (!_ownedProcess.WaitForExit(10000)) StopOwnedProcess(_ownedProcess);
                    }
                    finally
                    {
                        _ownedProcess.Dispose();
                    }
                }
            }
        }

        private static void StopOwnedProcess(Process process)
        {
            if (process == null) return;
            try
            {
                process.Refresh();
                if (process.HasExited) return;
                if (process.CloseMainWindow() && process.WaitForExit(3000)) return;
                process.Kill();
                process.WaitForExit(5000);
            }
            catch
            {
                // The exact process may already have exited between checks.
            }
        }
    }
}
