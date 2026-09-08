using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class CaptureUiLayouts
{
    private const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type settings = app.GetType("SWBodyOrganizer.UserSettingsStore"); settings.GetMethod("Load").Invoke(null, null);
        app.GetType("SWBodyOrganizer.Program").GetField("SuppressStartupPrompts", flags).SetValue(null, true);
        Directory.CreateDirectory(args[2]);
        foreach (string language in new[] { "zh-CN", "en-US" })
        {
            object current = settings.GetProperty("Current").GetValue(null, null); current.GetType().GetProperty("Language").SetValue(current, language, null);
            using (Form form = (Form)Activator.CreateInstance(app.GetType("SWBodyOrganizer.MainForm")))
            {
                Type type = form.GetType();
                type.GetMethod("LoadProjectForScreenshot", flags).Invoke(form, new object[] { Path.GetFullPath(args[1]), false, false });
                form.StartPosition = FormStartPosition.Manual; form.Left = -30000; form.Show(); Application.DoEvents();
                foreach (Size size in new[] { new Size(1540, 920), new Size(1100, 720) })
                {
                    form.Size = size; Application.DoEvents();
                    using (Bitmap image = new Bitmap(size.Width, size.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, size)); image.Save(Path.Combine(args[2], language + "-" + size.Width + ".png")); }
                }
                ((CheckBox)type.GetField("compactList", flags).GetValue(form)).Checked = true; Application.DoEvents();
                using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(args[2], language + "-compact-1100.png")); }
                type.GetMethod("CaptureGuidedScreenshot", flags).Invoke(form, new object[] { Path.GetFullPath(Path.Combine(args[2], language + "-guided.png")) });
                type.GetMethod("AllowCloseForSelfTest", flags).Invoke(form, null); form.Close();
            }
        }
        Console.WriteLine("Rendered both languages at 1540/1100, compact and guided. Saved-project UI only; no SolidWorks work performed.");
        return 0;
    }
}
