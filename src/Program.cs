using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Master Miao")]
[assembly: AssemblyDescription("读取、预览、分类并安全导出 SolidWorks 多实体零件与原位装配体")]
[assembly: AssemblyCompany("Master Miao")]
[assembly: AssemblyProduct("Master Miao")]
[assembly: AssemblyVersion("1.2.4.0")]
[assembly: ComVisible(false)]

namespace SWBodyOrganizer
{
    internal static class Program
    {
        internal static bool SuppressStartupPrompts;

        [STAThread]
        private static int Main(string[] args)
        {
            AppPaths.Ensure();
            UserSettingsStore.Load();
            if (string.IsNullOrWhiteSpace(UserSettingsStore.Current.Language)) UserSettingsStore.Current.Language = "zh-CN";
            if (args.Length == 3 && string.Equals(args[0], "--worker", StringComparison.OrdinalIgnoreCase))
                return WorkerMain.Run(args[1], args[2]);
            if (args.Length == 2 && (string.Equals(args[0], "--report-selftest", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "--report-selftest-en", StringComparison.OrdinalIgnoreCase)))
            {
                AppPaths.Ensure();
                string preview = Path.ChangeExtension(args[1], ".png");
                using (Bitmap bitmap = new Bitmap(420, 300))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.WhiteSmoke);
                    using (Pen pen = new Pen(Color.FromArgb(215, 25, 32), 8F)) graphics.DrawRectangle(pen, 72, 58, 276, 184);
                    using (Brush brush = new SolidBrush(Color.FromArgb(55, 61, 70)))
                    using (Font font = new Font("Arial", 20F, FontStyle.Bold)) graphics.DrawString("REPORT PREVIEW", font, brush, 92, 126);
                    bitmap.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
                }
                bool englishReport = string.Equals(args[0], "--report-selftest-en", StringComparison.OrdinalIgnoreCase);
                ExcelReportWriter.Create(args[1], new List<ExportResultItem>
                {
                    new ExportResultItem { ExportName = "示例零件", SourceName = "示例.SLDPRT", OriginalName = "实体1", CategoryPath = "结构件\\板件", Quantity = 2, PreviewIso = preview, PreviewFront = preview, PreviewTop = preview, SldprtStatus = "成功", StepStatus = "未启用", VerificationStatus = "单实体验证通过" }
                }, englishReport ? "Report self-test" : "报表自检", Path.GetDirectoryName(args[1]), englishReport ? "en-US" : "zh-CN");
                return 0;
            }
            if (args.Length == 2 && string.Equals(args[0], "--startup-selftest", StringComparison.OrdinalIgnoreCase))
                return StartupSelfTest(args[1]);
            if (args.Length == 2 && string.Equals(args[0], "--ui-screenshot", StringComparison.OrdinalIgnoreCase))
                return CaptureUi(args[1], string.Empty, false, false, 1540, 920);
            if (args.Length == 3 && string.Equals(args[0], "--ui-guided-screenshot", StringComparison.OrdinalIgnoreCase))
                return CaptureGuidedUi(args[1], args[2]);
            if (args.Length == 3 && string.Equals(args[0], "--ui-project-screenshot-en", StringComparison.OrdinalIgnoreCase))
            {
                UserSettingsStore.Current.Language = "en-US";
                return CaptureUi(args[2], args[1], false, false, 1540, 920);
            }
            if (args.Length == 3 && string.Equals(args[0], "--project-selftest", StringComparison.OrdinalIgnoreCase))
                return ProjectSelfTest(args[1], args[2]);
            if (args.Length == 2 && string.Equals(args[0], "--logic-selftest", StringComparison.OrdinalIgnoreCase))
                return LogicSelfTest(args[1]);
            if (args.Length == 3 && (string.Equals(args[0], "--ui-project-screenshot", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "--ui-small-project-screenshot", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "--ui-category-screenshot", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "--ui-relation-screenshot", StringComparison.OrdinalIgnoreCase)))
            {
                bool showRelation = string.Equals(args[0], "--ui-relation-screenshot", StringComparison.OrdinalIgnoreCase);
                bool showClassification = showRelation || string.Equals(args[0], "--ui-category-screenshot", StringComparison.OrdinalIgnoreCase);
                bool small = string.Equals(args[0], "--ui-small-project-screenshot", StringComparison.OrdinalIgnoreCase);
                return CaptureUi(args[2], args[1], showClassification, showRelation, small ? 1100 : 1540, small ? 720 : 920);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (UserSettingsStore.Current.AskLanguageOnStartup)
                {
                    using (LanguageDialog language = new LanguageDialog(true))
                        if (language.ShowDialog() == DialogResult.OK) language.ApplySelection();
                }
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "程序发生未处理错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int CaptureUi(string outputPath, string projectPath, bool showClassification, bool showRelation, int width, int height)
        {
            SuppressStartupPrompts = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                if (!string.IsNullOrWhiteSpace(projectPath)) form.LoadProjectForScreenshot(projectPath, showClassification, showRelation);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Size = new Size(width, height);
                form.Show();
                Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Close();
            }
            return 0;
        }

        private static int StartupSelfTest(string outputPath)
        {
            // Exercise the real startup branch: InitializeV120 must run while no
            // native handle exists, then the form must create its handle cleanly.
            SuppressStartupPrompts = false;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                if (form.IsHandleCreated) return 2;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                Application.DoEvents();
                if (!form.IsHandleCreated) return 3;
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                form.AllowCloseForSelfTest();
                form.Close();
            }
            return File.Exists(outputPath) ? 0 : 4;
        }

        private static int CaptureGuidedUi(string projectPath, string outputPath)
        {
            SuppressStartupPrompts = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                form.LoadProjectForScreenshot(projectPath, false, false);
                form.CaptureGuidedScreenshot(outputPath);
            }
            return File.Exists(outputPath) ? 0 : 1;
        }

        private static int ProjectSelfTest(string projectPath, string outputPath)
        {
            SuppressStartupPrompts = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                form.LoadProjectForScreenshot(projectPath, false, false);
                form.SaveProjectForSelfTest(outputPath);
            }
            AppProject saved = JsonFile.Load<AppProject>(outputPath);
            if (saved.SchemaVersion != 2 || saved.Sources.Count == 0 || saved.AllBodies().Any(body => !string.IsNullOrWhiteSpace(body.PreviewIso) && !File.Exists(body.PreviewIso))) return 2;
            return 0;
        }

        private static int LogicSelfTest(string projectPath)
        {
            SuppressStartupPrompts = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                form.LoadProjectForScreenshot(projectPath, false, false);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                Application.DoEvents();
                bool passed = form.RunLogicSelfTest();
                form.AllowCloseForSelfTest();
                form.Close();
                return passed ? 0 : 3;
            }
        }
    }
}
