using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SWBodyOrganizer
{
    internal static class HoleAddinInstaller
    {
        internal static void Install()
        {
            var folder = AppDomain.CurrentDomain.BaseDirectory;
            var script = Path.Combine(folder, "Install-HoleTools.ps1");
            foreach (var name in new[] { "Install-HoleTools.ps1", "Register-Addin.ps1", "Blankedge.SWHoleTools.dll",
                "SolidWorks.Interop.sldworks.dll", "SolidWorks.Interop.swconst.dll", "SolidWorks.Interop.swpublished.dll" })
                if (!File.Exists(Path.Combine(folder, name))) throw new FileNotFoundException("部署文件缺失：" + name);
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using (var process = Process.Start(info))
            {
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("插件部署失败，请检查安装包和管理员权限。错误码：" + process.ExitCode);
            }
            const string guid = "{6D68D92F-6A8C-45A0-9DCD-3B92AA5103D1}";
            // Apply user settings in the caller's profile even when UAC used another account.
            string clsid = @"Software\Classes\CLSID\" + guid + @"\InprocServer32";
            using (var machine = Registry.LocalMachine.OpenSubKey(clsid))
            using (var user = Registry.CurrentUser.OpenSubKey(clsid, true))
            {
                string path = machine?.GetValue("CodeBase") as string;
                if (path == null || !File.Exists(new Uri(path).LocalPath)) throw new IOException("未找到注册后的插件。");
                if (user != null)
                {
                    user.SetValue("CodeBase", path);
                    string version = System.Reflection.AssemblyName.GetAssemblyName(new Uri(path).LocalPath).Version.ToString();
                    using (var sub = user.CreateSubKey(version)) sub.SetValue("CodeBase", path);
                }
            }
            using (var startup = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\" + guid))
                startup.SetValue("", 1, RegistryValueKind.DWord);
        }
    }
}
