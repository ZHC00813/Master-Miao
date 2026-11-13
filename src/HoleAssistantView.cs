using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using Blankedge.SWHoleTools.UI;
using SolidWorks.Interop.sldworks;
namespace SWBodyOrganizer
{
    internal sealed class HoleAssistantView : HoleFilterView
    {
        public HoleAssistantView() : base(Connect, CreateInstallButton()) { }
        private static ISldWorks Connect() => (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        private static Button CreateInstallButton()
        {
            var install = new Button { Text = "一键部署到 SolidWorks", AutoSize = true, Height = 34 };
            install.Click += async (sender, e) =>
            {
                install.Enabled = false;
                install.Text = "正在部署…";
                try
                {
                    await System.Threading.Tasks.Task.Run(() => HoleAddinInstaller.Install());
                    
                    MessageBox.Show(install, "插件已安装并设为自动启动。请重新打开 SolidWorks。", "孔标注助手");
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                { MessageBox.Show(install, "已取消部署。"); }
                catch (Exception ex) { MessageBox.Show(install, ex.Message, "部署失败"); }
                finally { install.Enabled = true; install.Text = "一键部署到 SolidWorks"; }
            };
            return install;
        }
    }
}
