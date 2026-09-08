using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SWBodyOrganizer;

// Explicit desktop acceptance only. Owns a new SW instance and a NEW test directory;
// copies/saves only the supplied fixture, never the user's original or existing session.
internal static class VerifyLocatorAndDedup
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Call(string type, string method, params object[] args)
    { return typeof(AppProject).Assembly.GetType("SWBodyOrganizer." + type, true).GetMethod(method, Flags).Invoke(null, args); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [STAThread]
    private static int Main(string[] args)
    {
        ISldWorks app = null;
        string fixture = null, original = null, originalHash = null;
        int ownedPid = 0;
        try
        {
            Assert(args.Length == 2, "Usage: VerifyLocatorAndDedup <source part> <NEW test directory>");
            Assert(Process.GetProcessesByName("SLDWORKS").Length == 0, "An existing SW session is present; no desktop test was started.");
            original = Path.GetFullPath(args[0]); string root = Path.GetFullPath(args[1]);
            Assert(!Directory.Exists(root), "Test directory must be NEW; existing work is never overwritten.");
            originalHash = (string)Call("ExportIntegrity", "FileHash", original);
            Console.WriteLine("Original SHA-256 before test: " + originalHash);
            Directory.CreateDirectory(root); fixture = Path.Combine(root, "locator-fixture.SLDPRT");
            File.Copy(original, fixture, false);
            app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application", true));
            ownedPid = app.GetProcessID(); app.Visible = true;
            Console.WriteLine("Owned SW PID=" + ownedPid + ", revision=" + app.RevisionNumber());
            int errors = 0, warnings = 0;
            IModelDoc2 model = app.OpenDoc6(fixture, (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            Assert(model != null, "Cannot open isolated fixture: " + errors);
            // Normalize only the isolated copy if SW automatically rebuilt it.
            Assert(model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) && errors == 0, "Cannot save isolated fixture.");
            Marshal.ReleaseComObject(model); model = null;
            WorkerRequest request = new WorkerRequest { Operation = "scan", KeepSourceDocumentsOpen = true,
                GeneratePreviews = false, CacheRoot = Path.Combine(root, "cache"), CancelFile = Path.Combine(root, "cancel.signal") };
            request.Sources.Add(new SourceRecord { Path = fixture });
            WorkerResponse response = new WorkerResponse { TemplatePath = (string)Call("WorkerMain", "FindPartTemplate", app) };
            Call("WorkerMain", "Scan", app, request, response);
            Assert(response.Success && response.Sources.Count == 1, "Actual scan failed: " + response.Message);
            JsonFile.Save(Path.Combine(root, "scan-response.json"), response);
            List<BodyRecord> records = response.Sources[0].Bodies;
            Assert(records.Count > 1, "Fixture must contain multiple solid bodies.");
            object[] locateArgs = { new List<BodyRecord> { records[0], records[records.Count - 1] }, "" };
            Assert((bool)Call("SolidWorksLocator", "Highlight", locateArgs), "Live location failed: " + locateArgs[1]);
            model = app.GetOpenDocumentByName(fixture) as IModelDoc2;
            Assert(((ISelectionMgr)model.SelectionManager).GetSelectedObjectCount2(-1) == 2, "SW did not select both requested bodies.");
            Console.WriteLine("PASS: actual scan " + records.Count + " solids, live multi-body highlighting on writable-open source.");
            model.SetSaveFlag();
            Marshal.ReleaseComObject(model); model = null;
            locateArgs[0] = new List<BodyRecord> { records[records.Count / 2] };
            Assert((bool)Call("SolidWorksLocator", "Highlight", locateArgs), "Pending-save location failed: " + locateArgs[1]);
            model = app.GetOpenDocumentByName(fixture) as IModelDoc2;
            Assert(((ISelectionMgr)model.SelectionManager).GetSelectedObjectCount2(-1) == 1, "Dirty-state selection count differs.");
            bool exportRejected = false;
            try { Call("ExportIntegrity", "VerifyMemory", model, records[0].Configuration); }
            catch (TargetInvocationException ex) { exportRejected = ex.InnerException is InvalidDataException; }
            Assert(exportRejected, "Export must still reject pending-save state.");
            Console.WriteLine("PASS: pending-save document locates correctly; export remains protected.");
            string title = model.GetTitle(); Marshal.ReleaseComObject(model); model = null;
            app.CloseDoc(title); // Only our fixture; discard artificial dirty flag.

            AppProject project = new AppProject { Sources = response.Sources, OutputRoot = Path.Combine(root, "output") };
            project.Export.CreateAssembly = false; project.Export.ExportStep = false;
            project.Export.Deduplicate = true;
            List<BodyRecord> group = records.GroupBy(body => body.GeometryKey).First(g => g.Count() > 1).ToList();
            foreach (BodyRecord body in records) body.ExportSelected = group.Contains(body);
            foreach (BodyRecord body in group) body.ExportName = "Duplicate-test";
            string projectPath = Path.Combine(root, "project.swbody.json"); ProjectStore.Save(projectPath, project);
            UserSettingsStore.Load();
            typeof(AppProject).Assembly.GetType("SWBodyOrganizer.Program").GetField("SuppressStartupPrompts", Flags).SetValue(null, true);
            Application.EnableVisualStyles();
            using (Form form = (Form)Activator.CreateInstance(typeof(AppProject).Assembly.GetType("SWBodyOrganizer.MainForm")))
            {
                Type type = form.GetType();
                type.GetMethod("LoadProjectForScreenshot", Flags).Invoke(form, new object[] { projectPath, false, false });
                form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-32000, -32000);
                form.Show(); Application.DoEvents();
                DataGridView grid = (DataGridView)type.GetField("bodyGrid", Flags).GetValue(form);
                CheckBox dedup = (CheckBox)type.GetField("dedupCheck", Flags).GetValue(form);
                int folded = grid.Rows.Count;
                Assert(folded == records.Select(body => body.GeometryKey).Distinct().Count(), "Real-data grid did not fold all candidate groups.");
                dedup.Checked = false; Assert(grid.Rows.Count == records.Count, "Unchecking did not restore all actual solids.");
                dedup.Checked = true; Assert(grid.Rows.Count == folded, "Rechecking did not fold actual solids again.");
                Console.WriteLine("PASS: actual-data checkbox grid " + records.Count + " -> " + folded + " -> " + records.Count + " -> " + folded);
                object[] planArgs = { "" };
                request.ExportItems = (List<ExportPlanItem>)type.GetMethod("BuildExportPlan", Flags).Invoke(form, planArgs);
                Assert(request.ExportItems.Count == 1, "Duplicate plan failed: " + planArgs[0]);
                type.GetMethod("AllowCloseForSelfTest", Flags).Invoke(form, null); form.Close();
            }
            request.Operation = "export"; request.OutputRoot = project.OutputRoot;
            request.StagingRoot = Path.Combine(root, "staging"); request.ExportSettings = project.Export;
            request.CheckpointPath = Path.Combine(root, "export-checkpoint.json");
            response = new WorkerResponse { TemplatePath = response.TemplatePath };
            Call("WorkerMain", "Export", app, request, response);
            JsonFile.Save(Path.Combine(root, "export-response.json"), response);
            Assert(response.Success, "Duplicate export failed: " + string.Join("; ", response.ExportResults.Select(item => item.Message)));
            Assert(Directory.GetFiles(project.OutputRoot, "*.SLDPRT", SearchOption.AllDirectories).Length == 1 && response.ExportResults[0].Quantity == group.Count,
                "Duplicate output count or report quantity differs.");
            Console.WriteLine("PASS: " + group.Count + " verified duplicate solids exported as ONE SLDPRT, reopened and geometry checked.");
            Assert((string)Call("ExportIntegrity", "FileHash", original) == originalHash, "Original changed during test.");
            Console.WriteLine("PASS: original SHA-256 unchanged. Preview generation and STEP are outside this focused test.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (app != null)
            {
                // Never shut down an instance if the user added another document during testing.
                try
                {
                    object[] docs = app.GetDocuments() as object[] ?? new object[0];
                    bool onlyFixture = docs.OfType<IModelDoc2>().All(doc => string.Equals(doc.GetPathName(), fixture, StringComparison.OrdinalIgnoreCase));
                    if (app.GetProcessID() == ownedPid && onlyFixture) { if (fixture != null) app.CloseDoc(Path.GetFileName(fixture)); app.ExitApp(); }
                    else Console.WriteLine("SW left open because additional documents are present.");
                }
                catch (Exception ex) { Console.WriteLine("SW cleanup warning: " + ex.Message); }
                try { Marshal.ReleaseComObject(app); } catch { }
            }
        }
    }
}
