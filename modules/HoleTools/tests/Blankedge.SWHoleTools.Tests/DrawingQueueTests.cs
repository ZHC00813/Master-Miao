using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Blankedge.SWHoleTools.BatchExporter.Model;
using DrawingForm = Blankedge.SWHoleTools.BatchExporter.MainForm;

namespace Blankedge.SWHoleTools.Tests
{
    internal static class DrawingQueueTests
    {
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static object Field(object obj, string field) => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);
        private static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Invoke(obj, args);

        public static void Run(string sandbox)
        {
            Check(ModelViewNames.Resolve("*Front", new[] { "*前视", "*上视" }) == "*前视", "localized standard view failed");
            Check(ModelViewNames.Resolve("Custom", new[] { "Custom", "*Front" }) == "Custom", "custom view changed");
            string root = Path.Combine(sandbox, "queue");
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            string a = Path.Combine(root, "A.SLDPRT"), b = Path.Combine(root, "nested", "B.sldprt");
            File.WriteAllText(a, "test sentinel, not CAD"); File.WriteAllText(b, "test sentinel, not CAD");
            File.WriteAllText(Path.Combine(root, "ignored.txt"), "ignored");
            Check(PartFileDiscovery.Collect(new[] { root, a }, null).Count == 2, "recursive import or deduplication failed");
            foreach (string bad in new[] { "0", "1:0", "NaN", "Infinity", "1:2:3", "-1:2", "0.00001", "" })
            {
                bool rejected = false;
                try { DrawingExportJob.ApplyScale(new BatchExportOptions(), bad); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "invalid ratio accepted: " + bad);
            }
            var options = new BatchExportOptions { OutputDirectory = Path.Combine(root, "output"), NamePrefix = "DB101P-", SaveDwg = true, SaveDxf = true, SavePdf = true };
            var job = new DrawingExportJob { SourcePath = a, ScaleText = "1:2" };
            var snapshot = job.CreateOptions(options);
            Check(snapshot.FixedScale == 0.5 && snapshot.ScaleMode == ScaleChoice.Fixed, "scale conversion failed");
            options.Views.ModelViewNames.Clear();
            Check(snapshot.Views.ModelViewNames.Count > 0, "options are not isolated");
            Check(snapshot.OutputPaths(a).Count == 4 && snapshot.OutputPaths(a).All(path => Path.GetFileNameWithoutExtension(path) == "DB101P-A"), "prefix or formats failed");
            Check(snapshot.OutputPaths(a).Select(Path.GetDirectoryName).Distinct().Count() == 4, "format folders missing");
            string duplicate = Path.Combine(root, "nested", "A.SLDPRT");
            File.WriteAllText(duplicate, "sentinel");
            Check(DrawingExportJob.ValidateBatch(new[] { job, new DrawingExportJob { SourcePath = duplicate } }, snapshot).Any(error => error.Contains("重名")), "duplicate output accepted");
            snapshot.NamePrefix = "../";
            Check(snapshot.Validate().Count > 0, "unsafe prefix accepted");
            using (var form = new DrawingForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-32000, -32000);
                form.Show();
                Call(form, "AddFiles", (object)new[] { a, b });
                Application.DoEvents();
                var jobs = (BindingList<DrawingExportJob>)Field(form, "_jobs");
                var grid = (DataGridView)Field(form, "_files");
                var ratio = (TextBox)Field(form, "_batchScale");
                var scope = (ComboBox)Field(form, "_scaleScope");
                scope.SelectedIndex = 1; ratio.Text = "1:2"; Call(form, "ApplyBatchScale");
                Check(jobs.All(item => item.ScaleText == "1:2"), "all ratio application failed");
                scope.SelectedIndex = 0; grid.ClearSelection(); grid.Rows[0].Selected = true;
                ratio.Text = "2:1"; Call(form, "ApplyBatchScale");
                Check(jobs[0].ScaleText == "2:1" && jobs[1].ScaleText == "1:2", "selected ratio changed unselected row");
                grid.Rows[1].Cells[3].Value = "1:5";
                grid.EndEdit();
                Check(jobs[1].CreateOptions(new BatchExportOptions()).FixedScale == 0.2, "per-row edit not committed");
                ((TextBox)Field(form, "_prefix")).Text = "DB-";
                Check(jobs[0].ExportName == "DB-A" && jobs[1].ExportName == "DB-B", "live name preview failed");
                jobs[0].Included = false;
                var queue = (System.Collections.Generic.IList<DrawingExportJob>)Call(form, "ReadFiles");
                Check(queue.Count == 1 && queue[0].OriginalName == "B", "export checkbox ignored");
                jobs[1].ScaleText = "1:10";
                Check(queue[0].ScaleText == "1:5", "job snapshot mutated");
                var data = new DataObject(DataFormats.FileDrop, new[] { root });
                Call(form, "OnDragDrop", form, new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy));
                Check(jobs.Count == 3, "folder drop or deduplication failed");
                Call(form, "SetRunning", true);
                Check(!((Control)Field(form, "_editor")).Enabled, "running options still editable");
                Call(form, "SetRunning", false);
                form.Close();
            }
        }
    }
}
