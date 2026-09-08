using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SWBodyOrganizer;

// Actual isolated WinForms/file tests; no SolidWorks connection or CAD export.
internal static class VerifyR3Workflow
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    private static Type TypeOf(string name) { return typeof(MainForm).Assembly.GetType("SWBodyOrganizer." + name, true); }
    private static object Field(object value, string name) { return value.GetType().GetField(name, Flags).GetValue(value); }
    private static object Call(object value, string name, params object[] args) { return value.GetType().GetMethod(name, Flags).Invoke(value, args); }
    private static object Static(string type, string method, params object[] args) { return TypeOf(type).GetMethod(method, Flags).Invoke(null, args); }
    private static object Create(string type, params object[] args) { return Activator.CreateInstance(TypeOf(type), Flags, null, args, null); }
    private static void Assert(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); }
    private static void Shot(Form form, string path) { using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(path); } }
    private static List<ExportPlanItem> Plan(MainForm form, out string error)
    { object[] args = { "" }; var result = (List<ExportPlanItem>)Call(form, "BuildExportPlan", args); error = (string)args[0]; return result; }
    private static bool Key(object binding, Control target, Keys key, bool repeat)
    { object[] args = { Message.Create(target.Handle, 0x100, new IntPtr((int)key), new IntPtr(repeat ? 0x40000000 : 0)) }; return (bool)Call(binding, "PreFilterMessage", args); }
    private static void EditName(MainForm form, DataGridView grid, string text)
    {
        Call(form, "BeginExportNameEdit", grid, new DataGridViewCellEventArgs(grid.Columns["ExportName"].Index, grid.CurrentRow.Index));
        ((TextBox)Field(form, "exportNameEditor")).Text = text;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            UserSettingsStore.Load(); TypeOf("Program").GetField("SuppressStartupPrompts", Flags).SetValue(null, true);
            Application.EnableVisualStyles();
            foreach (string language in new[] { "zh-CN", "en-US" })
            {
                UserSettingsStore.Current.Language = language;
                using (MainForm form = new MainForm())
                {
                    form.StartPosition = FormStartPosition.Manual; form.Left = -30000;
                    AppProject project = (AppProject)Field(form, "project");
                    string sourcePath = Path.Combine(root, language + "-planning-fixture.SLDPRT"); File.WriteAllText(sourcePath, "Planning-only fixture, not CAD");
                    FileInfo info = new FileInfo(sourcePath);
                    SourceRecord source = new SourceRecord { Path = sourcePath, Name = "Fixture", Status = "读取完成", BodyCount = 120,
                        Length = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks, ContentSha256 = (string)Static("ExportIntegrity", "FileHash", sourcePath) };
                    for (int i = 0; i < 120; i++) source.Bodies.Add(new BodyRecord { Index = i, SourceId = source.Id, SourcePath = source.Path,
                        SourceName = source.Name, OriginalName = "Body_" + i, ExportName = "Part_" + i, ExportSelected = true, GeometryKey = "unique-" + i });
                    project.Sources.Add(source); project.OutputRoot = Path.Combine(root, language + "-output");
                    Call(form, "BindProject"); form.Show(); Application.DoEvents();
                    DataGridView grid = (DataGridView)Field(form, "bodyGrid");
                    source.Bodies[10].ExportName = ""; source.Bodies[80].ExportName = "";
                    ((TextBox)Field(form, "bodySearch")).Text = "not-visible";
                    string error; Assert(Plan(form, out error).Count == 0 && error.Length > 0, "Blank names not rejected");
                    Call(form, "ShowValidationIssues");
                    Assert(((BodyRecord)grid.CurrentRow.Tag).Id == source.Bodies[10].Id && grid.Rows.Count == 120 && grid.CurrentRow.Displayed && grid.CurrentRow.Selected, "First hidden issue not revealed/selected");
                    Assert(((IList)Field(form, "reviewIssues")).Count == 2, "Not all unnamed bodies collected");
                    Assert(((Label)Field(form, "previewDetailsLabel")).Text.Contains("Body_10"), "Preview did not follow issue focus");
                    Shot(form, Path.Combine(root, language + "-issue.png"));
                    EditName(form, grid, "Fixed_10"); Call(form, "MoveReview", 1);
                    Assert(source.Bodies[10].ExportName == "Fixed_10" && ((BodyRecord)grid.CurrentRow.Tag).Id == source.Bodies[80].Id && grid.CurrentRow.Displayed, "Next did not save and scroll to later issue");
                    EditName(form, grid, "Fixed_80"); Call(form, "MoveReview", 1);
                    Assert(!((Panel)Field(form, "issueNavigator")).Visible && source.Bodies[80].ExportName == "Fixed_80", "Issue completion lost edits or did not dismiss");

                    CategoryNode category = new CategoryNode { Name = "Metal", ParentId = CategoryNode.RootId }; project.Categories.Add(category);
                    source.Bodies[0].GeometryKey = source.Bodies[1].GeometryKey = "duplicate";
                    source.Bodies[1].CategoryId = category.Id;
                    ((CheckBox)Field(form, "dedupCheck")).Checked = true;
                    Plan(form, out error); Call(form, "ShowValidationIssues"); Call(form, "MoveReview", 1);
                    Assert(((BodyRecord)grid.CurrentRow.Tag).Id == source.Bodies[1].Id && grid.Rows.Count == 119 && project.Export.Deduplicate, "Hidden group member was not focused without changing dedup");
                    grid.CurrentRow.Cells["Category"].Value = CategoryNode.UnclassifiedId; Call(form, "MoveReview", 1);
                    Assert(source.Bodies[0].CategoryId == source.Bodies[1].CategoryId && !((Panel)Field(form, "issueNavigator")).Visible, "Category correction not committed to group");

                    WorkerResponse failed = new WorkerResponse();
                    failed.ExportResults.Add(new ExportResultItem { BodyId = source.Bodies[95].Id, Outcome = "失败", StepStatus = "失败", Message = "Late failure" });
                    failed.ExportResults.Add(new ExportResultItem { BodyId = source.Bodies[5].Id, Outcome = "失败", StepStatus = "失败", Message = "Early failure" });
                    Call(form, "ShowFailedItems", failed); Assert(((BodyRecord)grid.CurrentRow.Tag).Id == source.Bodies[5].Id, "Failed output order is not top to bottom");
                    Call(form, "MoveReview", 1); Assert(((BodyRecord)grid.CurrentRow.Tag).Id == source.Bodies[95].Id, "Next failed output not focused");
                    Call(form, "DismissIssues");

                    TestGuidedShortcut(form, project, source);
                    SourceRecord unread = new SourceRecord { Name = "Unread fixture", Status = "读取失败" };
                    project.Sources.Add(unread);
                    WorkerResponse scanFailure = new WorkerResponse(); scanFailure.Sources.Add(unread);
                    Call(form, "ShowScanIssues", scanFailure);
                    object sourceItem = ((ListBox)Field(form, "sourceList")).SelectedItem;
                    Assert((string)sourceItem.GetType().GetField("Id", Flags).GetValue(sourceItem) == unread.Id, "Failed empty source not selected");
                    Call(form, "DismissIssues"); project.Sources.Remove(unread); Call(form, "BindProject");

                    ((CheckBox)Field(form, "stepOnlyCheck")).Checked = true;
                    Assert(project.Export.StepOnly && project.Export.ExportSldprt && project.Export.ExportStep && !project.Export.CreateAssembly &&
                        !((CheckBox)Field(form, "sldprtCheck")).Checked && !((ComboBox)Field(form, "stepFolderCombo")).Enabled, "STEP-only UI/internal prerequisite state differs");
                    Assert(Plan(form, out error).Count > 0 && error.Length == 0, "STEP-only blocked by old format validation");
                    string saved = Path.Combine(root, language + "-saved", "project.swbody.json"); Call(form, "SaveProjectForSelfTest", saved);
                    Assert(ProjectStore.Load(saved).Export.StepOnly, "STEP-only setting not persisted");
                    ((CheckBox)Field(form, "assemblyCheck")).Checked = true;
                    Assert(!project.Export.StepOnly && !project.Export.Deduplicate && ((CheckBox)Field(form, "sldprtCheck")).Checked, "Native assembly must exit STEP-only and restore standard behavior");
                    form.Size = new Size(1100, 720); Shot(form, Path.Combine(root, language + "-small.png"));
                    TestProgress(form, root, language);
                    Call(form, "AllowCloseForSelfTest"); form.Close();
                }
                TestShortcuts(root, language);
                Console.WriteLine("PASS " + language + ": actual row navigation/edit commits, hidden duplicate member, failure order, STEP-only UI/persistence, modal progress and shortcuts.");
            }
            TestStepOnlyFiles(root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void TestProgress(Form parent, string root, string language)
    {
        Assert((int)Static("ExportProgressDialog", "TaskPercent", 99, "导出零件", true) == 71 &&
            (int)Static("ExportProgressDialog", "TaskPercent", 97, "归类 STEP", true) == 97 &&
            (int)Static("ExportProgressDialog", "TaskPercent", 99, "导出零件", false) == 99, "STEP phase weighting regressed");
        int phase = 0, cancelled = 0; Exception failure = null;
        using (Form dialog = (Form)Create("ExportProgressDialog", (Func<bool>)delegate { cancelled++; return true; }))
        using (Timer timer = new Timer { Interval = 600 })
        {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Left = -30000;
            timer.Tick += delegate
            {
                try
                {
                    if (phase == 0)
                    {
                        Assert(!IsWindowEnabled(parent.Handle), "Progress dialog does not block its owner");
                        Call(dialog, "UpdateProgress", 64, language == "en-US" ? "Exporting STEP" : "导出 STEP");
                        Call(dialog, "UpdateProgress", 10, "Waiting"); Assert(((ProgressBar)Field(dialog, "bar")).Value == 64, "Progress regressed");
                        dialog.Close(); Assert(dialog.Visible, "Close bypasses safe cancellation");
                    }
                    else if (phase == 1)
                    {
                        Shot(dialog, Path.Combine(root, language + "-progress.png"));
                        ((Button)Field(dialog, "cancel")).PerformClick(); ((Button)Field(dialog, "cancel")).PerformClick();
                        Assert(cancelled == 1 && !((Button)Field(dialog, "cancel")).Enabled, "Cancellation is not one-shot");
                        Call(dialog, "UpdateProgress", 100, "Complete"); Assert(((ProgressBar)Field(dialog, "bar")).Value == 99, "Premature 100 percent during active work");
                    }
                    else { timer.Stop(); Call(dialog, "Finish"); }
                    phase++;
                }
                catch (Exception ex) { failure = ex; timer.Stop(); Call(dialog, "Finish"); }
            };
            timer.Start(); dialog.ShowDialog(parent);
            if (failure != null) throw failure;
            Assert(phase == 3 && IsWindowEnabled(parent.Handle), "Owner not restored after progress closes");
        }
    }
    private static void TestGuidedShortcut(MainForm form, AppProject project, SourceRecord source)
    {
        UserSettingsStore.Current.DeduplicateShortcut = (int)Keys.F7;
        project.GuidedIndex = 0; Exception failure = null; bool ran = false;
        using (Timer timer = new Timer { Interval = 100 })
        {
            timer.Tick += delegate
            {
                Form guided = Application.OpenForms.Cast<Form>().FirstOrDefault(value => value.GetType().Name == "GuidedBodyForm");
                if (guided == null) return;
                timer.Stop();
                try
                {
                    TextBox editor = (TextBox)Field(guided, "exportName");
                    editor.Text = "Shortcut_saved";
                    CheckBox selection = (CheckBox)Field(guided, "selected"); guided.ActiveControl = selection; selection.Focus();
                    Message key = Message.Create(selection.Handle, 0x100, new IntPtr((int)Keys.F7), IntPtr.Zero);
                    Assert(Application.FilterMessage(ref key), "Production guided shortcut was not dispatched: focus=" + guided.ActiveControl + ", enabled=" + guided.Enabled + ", key=" + UserSettingsStore.Current.DeduplicateShortcut + ", modifiers=" + Control.ModifierKeys);
                    Assert(!project.Export.Deduplicate && ((IList)Field(guided, "bodies")).Count == 120 && source.Bodies[0].ExportName == "Shortcut_saved", "Guided toggle lost draft or failed to restore bodies");
                    guided.ActiveControl = selection; selection.Focus(); key = Message.Create(selection.Handle, 0x100, new IntPtr((int)Keys.F7), IntPtr.Zero);
                    Assert(Application.FilterMessage(ref key), "Second production guided shortcut not dispatched");
                    Assert(project.Export.Deduplicate && ((IList)Field(guided, "bodies")).Count == 119 && source.Bodies.Count == 120, "Guided folding changed project records");
                    ran = true;
                }
                catch (Exception ex) { failure = ex; }
                finally { guided.Close(); }
            };
            timer.Start(); Call(form, "OpenGuidedMode", null, EventArgs.Empty);
        }
        if (failure != null) throw failure;
        Assert(ran, "Guided shortcut test did not run");
    }
    private static void TestShortcuts(string root, string language)
    {
        UserSettingsStore.Current.LocateShortcut = (int)Keys.F6; UserSettingsStore.Current.DeduplicateShortcut = (int)Keys.F7;
        int located = 0, toggled = 0; bool busy = false;
        using (Form owner = new Form { Left = -30000, StartPosition = FormStartPosition.Manual })
        {
            Button button = new Button(); TextBox typing = new TextBox { Top = 40 }; owner.Controls.AddRange(new Control[] { button, typing });
            object binding = Create("ShortcutBinding", owner, (Func<bool>)(() => !busy), (Action)(() => located++), (Action)(() => toggled++));
            owner.Show(); owner.ActiveControl = button;
            Assert(Key(binding, button, Keys.F6, false) && located == 1, "Locate key did not dispatch");
            Assert(Key(binding, button, Keys.F7, false) && toggled == 1, "Dedup key did not dispatch");
            Assert(Key(binding, button, Keys.F7, true) && toggled == 1, "Held key repeatedly toggled dedup");
            owner.ActiveControl = typing; Assert(!Key(binding, typing, Keys.F6, false) && located == 1, "Shortcut interrupted text/IME editing");
            owner.ActiveControl = button; busy = true; Assert(!Key(binding, button, Keys.F7, false) && toggled == 1, "Shortcut ran during work"); busy = false;
            Assert(((string)Static("ShortcutBinding", "Validate", Keys.F6, Keys.F6)).Length > 0, "Duplicate shortcuts accepted");
            Assert(((string)Static("ShortcutBinding", "Validate", Keys.Control | Keys.C, Keys.F7)).Length > 0, "Copy shortcut accepted");
            using (Form settings = (Form)Create("LanguageDialog", false))
            {
                object first = Field(settings, "locateKey"), second = Field(settings, "dedupKey");
                first.GetType().GetProperty("Shortcut", Flags).SetValue(first, Keys.F8, null);
                second.GetType().GetProperty("Shortcut", Flags).SetValue(second, Keys.F9, null);
                settings.StartPosition = FormStartPosition.Manual; settings.Left = -30000; settings.Show(); Application.DoEvents();
                Shot(settings, Path.Combine(root, language + "-settings.png")); Call(settings, "ApplySelection"); settings.Close();
            }
            UserSettingsStore.Load(); Assert(UserSettingsStore.Current.LocateShortcut == (int)Keys.F8 && UserSettingsStore.Current.DeduplicateShortcut == (int)Keys.F9, "Shortcut settings not persisted");
            using (Form startup = (Form)Create("LanguageDialog", true)) Call(startup, "ApplySelection");
            UserSettingsStore.Load(); Assert(UserSettingsStore.Current.LocateShortcut == (int)Keys.F8, "Startup language selection erased shortcuts");
            owner.Close(); ((IDisposable)binding).Dispose();
        }
    }
    private static void TestStepOnlyFiles(string root)
    {
        WorkerRequest request = new WorkerRequest { OutputRoot = Path.Combine(root, "step-only-output"), StagingRoot = Path.Combine(root, "step-only-stage") };
        request.ExportSettings.StepOnly = true; request.ExportSettings.ExportStep = true; request.ExportSettings.SeparateStepOutput = true;
        var result = new ExportResultItem { BodyId = "part", SourcePath = Path.Combine(root, "source-guard.SLDPRT"), PlannedExportName = "Part", CategoryPath = "A\\B", StepStatus = "成功", SldprtStatus = "成功" };
        File.WriteAllText(result.SourcePath, "original sentinel");
        Static("WorkerMain", "PlanOutputPaths", request, new List<ExportResultItem> { result });
        Assert(result.SldprtPath.StartsWith(request.StagingRoot + "\\", StringComparison.OrdinalIgnoreCase) && result.StepPath == Path.Combine(request.OutputRoot, "A\\B\\Part.STEP"), "STEP-only paths not separated");
        Directory.CreateDirectory(Path.GetDirectoryName(result.SldprtPath)); File.WriteAllText(result.SldprtPath, "temporary prerequisite");
        Directory.CreateDirectory(Path.GetDirectoryName(result.StepPath)); File.WriteAllText(result.StepPath, "STEP sentinel, not CAD");
        string temporary = result.SldprtPath;
        WorkerResponse response = new WorkerResponse(); response.ExportResults.Add(result);
        response.ExportResults.Add(new ExportResultItem { SldprtPath = result.SourcePath, SldprtStatus = "成功", StepStatus = "失败" });
        Static("WorkerMain", "FinishStepOnly", request, response, true);
        Assert(!File.Exists(temporary) && File.ReadAllText(result.SourcePath) == "original sentinel" && File.Exists(result.StepPath), "Cleanup touched an original/final file or left its own prerequisite");
        Assert(result.SldprtPath == "" && result.SldprtStatus == "未启用" && result.Outcome == "本次成功", "Temporary SLDPRT counted as delivery or successful STEP marked failed");
        Assert(Directory.GetFiles(request.OutputRoot, "*.SLDPRT", SearchOption.AllDirectories).Length == 0, "SLDPRT leaked into final output");
        File.WriteAllText(temporary, "live macro prerequisite");
        result.SldprtPath = temporary; result.SldprtStatus = "成功"; result.StepStatus = "待批量导出";
        request.TaskId = response.TaskId = "recovery-only-step"; request.CheckpointPath = Path.Combine(root, "step-only-checkpoint.json");
        string requestPath = Path.Combine(root, "step-only-request.json");
        JsonFile.Save(requestPath, request); JsonFile.Save(request.CheckpointPath, response);
        WorkerResponse recovered = (WorkerResponse)Static("MainForm", "RecoverWorkerResult", requestPath, Path.Combine(root, "absent-response.json"), "Injected worker exit");
        Assert(File.Exists(temporary) && recovered.ExportResults[0].SldprtPath == "" && recovered.ExportResults[0].StepStatus == "失败" && !recovered.Success, "Crash recovery deleted live prerequisite or counted it as delivery");
        Console.WriteLine("PASS: STEP-only staged paths, exact-file cleanup, source/final STEP preservation and result semantics. Files are sentinels, not a CAD export test.");
    }
}
