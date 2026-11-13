using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SWBodyOrganizer
{
    internal static class WorkspaceSelfTest
    {
        private static string tracePath;
        internal static void Trace(string text) { if (tracePath != null) File.AppendAllText(tracePath, text + "\n"); }
        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            tracePath = Path.Combine(output, "trace-detail.txt");
            Application.ThreadException += (sender, e) => { Trace(e.Exception.ToString()); Environment.Exit(1); };
            Program.SuppressStartupPrompts = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var shell = new WorkspaceShell())
            {
                shell.StartPosition = FormStartPosition.Manual;
                shell.Location = new Point(-32000, -32000);
                shell.Show();
                var bodyView = Descendants(shell).OfType<MainForm>().Single();
                foreach (var size in new[] { new Size(1540, 960), new Size(1280, 860) })
                {
                    shell.Size = size;
                    foreach (string id in new[] { "body-export", "drawings", "body-export", "drawings" })
                    {
                        if (!shell.ActivateModule(id) || shell.ActiveModuleId != id) throw new InvalidOperationException("Module switch failed: " + id);
                        if (id == "body-export" && !ReferenceEquals(bodyView, Descendants(shell).OfType<MainForm>().Single()))
                            throw new InvalidOperationException("Body module state was recreated.");
                        Application.DoEvents();
                        Shot(shell, Path.Combine(output, id + "-" + size.Width + ".png"));
                    }
                    foreach (var id in new[] { "holes", "drawing-model" })
                    {
                        if (!shell.ActivateModule(id)) throw new InvalidOperationException("Module switch failed: "+id);
                        Application.DoEvents();
                        Shot(shell,Path.Combine(output,id+"-"+size.Width+".png"));
                    }
                    if (!Descendants(shell).OfType<ComboBox>().Any(c=>c.AccessibleName=="标签分组"))
                        throw new InvalidOperationException("Missing hole tag group selector.");
                }
                File.AppendAllText(Path.Combine(output, "trace.txt"), "real close begin\n");
                shell.Close();
                File.AppendAllText(Path.Combine(output, "trace.txt"), "real close end\n");
                if (!shell.IsDisposed) throw new InvalidOperationException("Shell failed to close.");
            }
            var first = new StubModule("first");
            var second = new StubModule("second");
            var registry = new ModuleRegistry();
            registry.Register(() => first);
            registry.Register(() => second);
            using (var shell = new WorkspaceShell(registry))
            {
                shell.StartPosition = FormStartPosition.Manual;
                shell.Location = new Point(-32000, -32000);
                File.AppendAllText(Path.Combine(output, "trace.txt"), "stub show\n");
                shell.Show();
                first.Busy = true;
                if (shell.ActivateModule("second")) throw new InvalidOperationException("Busy module allowed switch.");
                shell.Close();
                File.AppendAllText(Path.Combine(output, "trace.txt"), "stub busy close\n");
                if (shell.IsDisposed) throw new InvalidOperationException("Busy module allowed close.");
                first.Busy = false;
                first.AllowClose = false;
                shell.Close();
                if (shell.IsDisposed) throw new InvalidOperationException("Close cancellation ignored.");
                first.AllowClose = true;
                if (!shell.ActivateModule("second") || !shell.ActivateModule("first")) throw new InvalidOperationException("Registry navigation failed.");
                shell.Close();
            }
            File.WriteAllText(Path.Combine(output, "modules-result.txt"), "PASS real module switch/state, two window sizes, close; fake module busy/close guards. No CAD operation executed.");
            return 0;
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
        }
        private static void Shot(Form form, string path)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path); }
        }
        private sealed class StubModule : IWorkspaceModule
        {
            public StubModule(string id) { Id = id; }
            public string Id { get; private set; }
            public string Title { get { return Id; } }
            public Control View { get; } = new Panel();
            public bool Busy;
            public bool AllowClose = true;
            public bool IsBusy { get { return Busy; } }
            public bool CanClose() { return AllowClose; }
            public void Dispose() { View.Dispose(); }
        }
    }
}
