using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Drives a real WinForms message loop and autosave timer against an isolated
// executable. Enter is simulated; actual Chinese IME composition remains a
// separate visible-desktop acceptance item.
internal static class VerifyEditingPersistence
{
    private static Assembly app;
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Field(object value, string name) { return value.GetType().GetField(name, flags).GetValue(value); }
    private static object Get(object value, string name) { return value.GetType().GetProperty(name).GetValue(value, null); }
    private static void Set(object value, string name, object data) { value.GetType().GetProperty(name).SetValue(value, data, null); }
    private static object Call(object value, string name, params object[] args) { foreach (MethodInfo method in value.GetType().GetMethods(flags)) if (method.Name == name && method.GetParameters().Length == args.Length) return method.Invoke(value, args); throw new MissingMethodException(name); }
    private static object New(string name) { return Activator.CreateInstance(app.GetType("SWBodyOrganizer." + name)); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pump(int milliseconds) { Stopwatch timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
    private static void SendEnterToEditor(TextBox editor)
    {
        Message enter = Message.Create(editor.Handle, 0x100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
        Assert(!editor.PreProcessMessage(ref enter), "Enter escaped from the editor to a dialog/navigation command.");
        SendMessage(editor.Handle, 0x100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
        SendMessage(editor.Handle, 0x102, new IntPtr('\r'), IntPtr.Zero);
        SendMessage(editor.Handle, 0x101, new IntPtr((int)Keys.Enter), IntPtr.Zero);
        Assert(!editor.Text.Contains("\r") && !editor.Text.Contains("\n"), "Enter inserted a newline into the name.");
    }
    private static Button FindButton(Control root, params string[] captions)
    {
        foreach (Control child in root.Controls)
        {
            Button button = child as Button;
            if (button != null && captions.Any(caption => string.Equals(button.Text, caption, StringComparison.OrdinalIgnoreCase))) return button;
            Button nested = FindButton(child, captions);
            if (nested != null) return nested;
        }
        return null;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
        app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        app.GetType("SWBodyOrganizer.UserSettingsStore").GetMethod("Load").Invoke(null, null);
        app.GetType("SWBodyOrganizer.Program").GetField("SuppressStartupPrompts", flags).SetValue(null, true);
        Form form = null;
        bool expectedBaselineFailure = args.Length > 2 && args[2] == "baseline";
        try
        {
            Console.WriteLine("Constructing actual WinForms window from " + args[0]);
            form = (Form)New("MainForm");
            form.StartPosition = FormStartPosition.Manual; form.Left = -30000;
            object project = Field(form, "project"), source = New("SourceRecord"), first = New("BodyRecord"), second = New("BodyRecord"), third = New("BodyRecord");
            Set(source, "Name", "Synthetic bodies (no SolidWorks geometry)");
            Set(source, "Path", Path.Combine(root, "nonexistent-test-model.SLDPRT"));
            int index = 0;
            foreach (object body in new[] { first, second, third }) { Set(body, "SourceId", Get(source, "Id")); Set(body, "SourceName", Get(source, "Name")); Set(body, "Index", index); Set(body, "OriginalName", "Body_" + index); Set(body, "ExportName", "Part_" + index++); ((IList)Get(source, "Bodies")).Add(body); }
            ((IList)Get(project, "Sources")).Add(source);
            Call(form, "BindProject"); form.Show(); Pump(200);
            DataGridView grid = (DataGridView)Field(form, "bodyGrid"); TextBox editor = (TextBox)Field(form, "exportNameEditor");
            MethodInfo isInputKey = editor.GetType().GetMethod("IsInputKey", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!expectedBaselineFailure) Assert(isInputKey != null && (bool)isInputKey.Invoke(editor, new object[] { Keys.Enter }), "Inline name editor does not own Enter; an IME confirmation can escape to the grid/dialog.");
            for (int saved = 0; saved < 2; saved++)
            {
                if (saved == 1) Call(form, "SaveProjectForSelfTest", Path.Combine(root, "saved", "project.swbody.json"));
                Call(form, "MarkProjectDirty");
                string original = (string)Get(first, "ExportName");
                Call(form, "BeginExportNameEdit", form, new DataGridViewCellEventArgs(grid.Columns["ExportName"].Index, 0));
                editor.Text = "草稿";
                Stopwatch edit = Stopwatch.StartNew(); int entered = 0;
                while (edit.ElapsedMilliseconds < 11200)
                {
                    editor.AppendText((entered++ % 2 == 0) ? "中" : "文");
                    Call(form, "ExportNameEditorKeyDown", editor, new KeyEventArgs(Keys.Enter));
                    if (!expectedBaselineFailure) SendEnterToEditor(editor);
                    Pump(300);
                    Assert(editor.Visible && (string)Get(first, "ExportName") == original, "AUTOSAVE interrupted draft or wrote unconfirmed input (saved=" + saved + ").");
                }
                string committed = editor.Text; Call(form, "FinishExportNameEdit", form, EventArgs.Empty);
                Assert((string)Get(first, "ExportName") == committed, "Finish naming did not commit.");
                Call(form, "BeginExportNameEdit", form, new DataGridViewCellEventArgs(grid.Columns["ExportName"].Index, 0)); editor.Text = "取消草稿";
                Call(form, "ExportNameEditorKeyDown", editor, new KeyEventArgs(Keys.Escape));
                Assert((string)Get(first, "ExportName") == committed, "Esc changed committed state.");
                Console.WriteLine("PASS: " + (saved == 0 ? "unsaved" : "saved") + " project, 11.2s across four autosave periods, Enter preserved draft, Finish/Esc semantics.");
            }
            Pump(500);
            object category = New("CategoryNode"); Set(category, "Name", "Classification"); Set(category, "ParentId", "root"); ((IList)Get(project, "Categories")).Add(category);
            // Invoke the actual guided form and Save-and-next path. Main grid is
            // deliberately left stale while autosave crosses a timer boundary.
            Type guidedType = app.GetType("SWBodyOrganizer.GuidedBodyForm");
            MethodInfo groupMethod = form.GetType().GetMethod("GetGroupMembers", flags), locateMethod = form.GetType().GetMethod("LocateBodiesInSolidWorks", flags);
            ConstructorInfo constructor = guidedType.GetConstructors()[0]; ParameterInfo[] parameters = constructor.GetParameters();
            Delegate groupProvider = Delegate.CreateDelegate(parameters[2].ParameterType, form, groupMethod);
            Delegate locator = Delegate.CreateDelegate(parameters[4].ParameterType, form, locateMethod);
            Action changed = delegate { Call(form, "MarkProjectDirty"); };
            object[] guidedArgs = parameters.Length == 7 ? new object[] { project, Get(source, "Bodies"), groupProvider, changed, locator, (Action)delegate { }, null } : parameters.Length == 6 ? new object[] { project, Get(source, "Bodies"), groupProvider, changed, locator, (Action)delegate { } } : new object[] { project, Get(source, "Bodies"), groupProvider, changed, locator };
            using (Form guided = (Form)constructor.Invoke(guidedArgs))
            {
                guided.StartPosition = FormStartPosition.Manual; guided.Left = -30000; guided.Show(); Pump(100);
                ((TextBox)Field(guided, "exportName")).Text = "逐项保存名称";
                ((ComboBox)Field(guided, "category")).SelectedValue = Get(category, "Id");
                Button saveNext = FindButton(guided, "保存并下一个", "Save and next");
                Button previous = FindButton(guided, "上一个", "Previous");
                Assert(saveNext != null && previous != null, "Guided navigation buttons not found.");
                saveNext.PerformClick(); Pump(3300); previous.PerformClick();
                Assert((string)Get(first, "ExportName") == "逐项保存名称" && (string)Get(first, "CategoryId") == (string)Get(category, "Id"), "Guided edits overwritten by stale grid/autosave.");
                Assert(((TextBox)Field(guided, "exportName")).Text == "逐项保存名称", "Guided reload lost name.");
                guided.Close();
            }
            Call(form, "CommitGrid"); Assert((string)Get(first, "ExportName") == "逐项保存名称", "CommitGrid replayed stale data.");
            Call(form, "SaveProjectForSelfTest", Path.Combine(root, "roundtrip", "project.swbody.json"));
            string json = File.ReadAllText(Path.Combine(root, "roundtrip", "project.swbody.json")); Assert(json.Contains("逐项保存名称"), "Saved guided state absent.");
            Console.WriteLine("PASS: actual guided Save-and-next/Previous buttons, 3.3s autosave, stale main rows, and saved JSON retain model state.");
            // Exercise the production modal entry point and its real main-list sync
            // callback as well as the stale-grid case above. This catches failures
            // hidden by calling Commit directly with a simplified callback.
            Exception modalFailure = null;
            int modalStep = 0;
            bool drivingModal = false;
            Set(project, "GuidedIndex", 0);
            Call(form, "RefreshGrid");
            using (var driver = new System.Windows.Forms.Timer { Interval = 350 })
            {
                driver.Tick += delegate
                {
                    if (drivingModal) return;
                    Form guided = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window.GetType() == guidedType);
                    if (guided == null) return;
                    drivingModal = true;
                    try
                    {
                        TextBox name = (TextBox)Field(guided, "exportName");
                        ComboBox folder = (ComboBox)Field(guided, "category");
                        CheckBox include = (CheckBox)Field(guided, "selected");
                        if (modalStep < 3)
                        {
                            name.Text = "按钮保存_" + modalStep;
                            folder.SelectedValue = Get(category, "Id");
                            include.Checked = modalStep != 1;
                            SendEnterToEditor(name);
                            FindButton(guided, "保存并下一个", "Save and next").PerformClick();
                            Assert((int)Field(guided, "index") == Math.Min(2, modalStep + 1), "Save and next did not advance to the expected body.");
                            modalStep++;
                            driver.Interval = modalStep == 1 ? 3200 : 350;
                            return;
                        }
                        object[] expected = { first, second, third };
                        for (int i = 0; i < expected.Length; i++)
                        {
                            Assert((string)Get(expected[i], "ExportName") == "按钮保存_" + i, "Modal navigation lost an edited name.");
                            Assert((string)Get(expected[i], "CategoryId") == (string)Get(category, "Id"), "Modal navigation lost a category.");
                            Assert((bool)Get(expected[i], "ExportSelected") == (i != 1), "Modal navigation lost export selection.");
                            DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().Single(item => ReferenceEquals(item.Tag, expected[i]));
                            Assert(Convert.ToString(row.Cells["ExportName"].Value) == "按钮保存_" + i && Convert.ToString(row.Cells["Category"].Value) == (string)Get(category, "Id"), "Actual guided callback did not return the committed name/category to the main list.");
                        }
                        FindButton(guided, "返回列表", "Back to list").PerformClick();
                        driver.Stop();
                    }
                    catch (Exception ex) { modalFailure = ex; driver.Stop(); guided.Close(); }
                    finally { drivingModal = false; }
                };
                driver.Start();
                Call(form, "OpenGuidedMode", form, EventArgs.Empty);
            }
            if (modalFailure != null) throw modalFailure;
            Assert(modalStep == 3, "The production guided workflow did not execute all three edits.");
            Call(form, "SaveProjectForSelfTest", Path.Combine(root, "modal-roundtrip", "project.swbody.json"));
            object reloaded = app.GetType("SWBodyOrganizer.ProjectStore").GetMethod("Load", flags).Invoke(null, new object[] { Path.Combine(root, "modal-roundtrip", "project.swbody.json") });
            IList reloadedBodies = (IList)Get(((IList)Get(reloaded, "Sources"))[0], "Bodies");
            for (int i = 0; i < 3; i++) Assert((string)Get(reloadedBodies[i], "ExportName") == "按钮保存_" + i && (string)Get(reloadedBodies[i], "CategoryId") == (string)Get(category, "Id") && (bool)Get(reloadedBodies[i], "ExportSelected") == (i != 1), "Saved/reopened project disagrees with guided values.");
            Console.WriteLine("PASS: production modal entry, native Enter message routing, three actual Save-and-next clicks, last item, main-list refresh, Back to list and disk reopen.");
            // Folding is reversible display behavior, not proof of CAD congruence.
            Set(first, "GeometryKey", "candidate-collision"); Set(second, "GeometryKey", "candidate-collision"); Set(third, "GeometryKey", "candidate-collision");
            CheckBox dedup = (CheckBox)Field(form, "dedupCheck");
            dedup.Checked = true;
            Assert(((IList)Call(form, "GetDisplayBodies", "")).Count == 1 && grid.Rows.Count == 1, "Checkbox did not immediately fold candidates in the actual grid.");
            ((CheckBox)Field(form, "assemblyCheck")).Checked = true;
            Assert(!dedup.Checked && grid.Rows.Count == 3, "Enabling assembly must disable dedup and restore rows immediately.");
            dedup.Checked = true;
            Assert(!((CheckBox)Field(form, "assemblyCheck")).Checked && grid.Rows.Count == 1, "Re-enabling dedup must disable assembly and fold rows.");
            dedup.Checked = false;
            Assert(grid.Rows.Count == 3 && ((IList)Get(source, "Bodies")).Count == 3 && (string)Get(second, "ExportName") == "按钮保存_1" && !(bool)Get(second, "ExportSelected"), "Unchecking lost an original body/edit/selection.");
            Set(third, "CandidateSuppressed", true); dedup.Checked = true;
            Assert(grid.Rows.Count == 2, "Excluded duplicate must remain separate.");
            Set(first, "ExportSelected", false); Set(second, "ExportSelected", true);
            Assert(object.ReferenceEquals(((IList)Call(form, "GetDisplayBodies", ""))[0], second), "Visible representative must match the first selected export member.");
            Set(first, "ConfirmedDuplicateGroupId", "manual-1"); Set(second, "ConfirmedDuplicateGroupId", "manual-1");
            Assert(((IList)Call(form, "GetDisplayBodies", "")).Count == 2, "Explicit group did not collapse.");
            Call(form, "ApplyToGroup", first, "Confirmed_name", Get(category, "Id"), false);
            Assert((string)Get(second, "ExportName") == "Confirmed_name" && (string)Get(third, "ExportName") != "Confirmed_name", "Confirmed group edited another candidate.");
            Console.WriteLine("PASS: actual checkbox folds/restores rows without deleting bodies or edits; exclusions, selected representative and explicit groups work. Not a geometry-recognition test.");
            Call(form, "RememberUndo"); Set(first, "ExportName", "Undo_this"); Call(form, "UndoEdit"); Assert((string)Get(first, "ExportName") == "Confirmed_name", "Undo failed.");
            ((CheckBox)Field(form, "stepCheck")).Checked = true; ((CheckBox)Field(form, "reportCheck")).Checked = false; ((ComboBox)Field(form, "conflictCombo")).SelectedIndex = 1;
            Assert((bool)Get(Get(project, "Export"), "ExportStep") && !(bool)Get(Get(project, "Export"), "CreateExcel") && (string)Get(Get(project, "Export"), "ConflictPolicy") == "自动编号", "Options did not synchronize before export.");
            Call(form, "SaveProjectForSelfTest", Path.Combine(root, "options", "project.swbody.json"));
            Console.WriteLine("PASS: undo and export option model synchronization before export.");
            object failedSource = New("SourceRecord"); Set(failedSource, "Id", Get(source, "Id")); Set(failedSource, "Status", "读取失败");
            object untouched = New("SourceRecord"); Set(untouched, "Name", "Unvisited after cancellation"); ((IList)Get(project, "Sources")).Add(untouched);
            object scanResponse = New("WorkerResponse"); ((IList)Get(scanResponse, "Sources")).Add(failedSource);
            Call(form, "ApplyScanResponse", scanResponse);
            Assert(((IList)Get(project, "Sources")).Count == 2 && object.ReferenceEquals(((IList)Get(project, "Sources"))[0], source) && ((IList)Get(source, "Bodies")).Count == 3 && (string)Get(first, "ExportName") == "Confirmed_name", "Failed/partial scan removed prior classification or unvisited files.");
            Assert((string)Get(source, "Status") == "读取失败", "Failed source not marked for rescan.");
            Console.WriteLine("PASS: partial/failed scan preserves prior body edits and unvisited sources, and marks failed source for rescan.");
            // Planning-only fake source: tests name-policy UI handoff, not CAD export.
            string planningFile = Path.Combine(root, "planning-only.SLDPRT"); File.WriteAllText(planningFile, "not a CAD model; UI planning test only");
            Set(source, "Path", planningFile); Set(source, "Status", "读取完成"); Set(source, "Length", new FileInfo(planningFile).Length); Set(source, "LastWriteTicks", File.GetLastWriteTimeUtc(planningFile).Ticks);
            Set(source, "ContentSha256", app.GetType("SWBodyOrganizer.ExportIntegrity").GetMethod("FileHash", flags).Invoke(null, new object[] { planningFile }));
            Set(project, "OutputRoot", Path.Combine(root, "planned-output")); Set(first, "ExportSelected", true); Set(second, "ExportSelected", true); Set(third, "ExportSelected", true); Set(third, "ExportName", Get(first, "ExportName")); Set(third, "CategoryId", Get(first, "CategoryId"));
            ((CheckBox)Field(form, "dedupCheck")).Checked = true;
            object[] planArgs = { "" }; IList plans = (IList)form.GetType().GetMethod("BuildExportPlan", flags).Invoke(form, planArgs);
            Assert(plans.Count == 2 && string.IsNullOrEmpty((string)planArgs[0]), "UI blocked duplicate names despite auto-number policy: " + planArgs[0] + " (count=" + plans.Count + ").");
            Assert((int)Get(plans[0], "Quantity") == 2 && ((IList)Get(plans[0], "DuplicateMembers")).Count == 1 && (string)Get(((IList)Get(plans[0], "DuplicateMembers"))[0], "Id") == (string)Get(second, "Id"), "Export omitted folded member identities needed for geometry verification.");
            Set(Get(project, "Export"), "ConflictPolicy", "跳过"); planArgs[0] = ""; form.GetType().GetMethod("BuildExportPlan", flags).Invoke(form, planArgs);
            Assert(!string.IsNullOrEmpty((string)planArgs[0]), "Non-numbering policy accepted duplicate planned names.");
            Console.WriteLine("PASS: duplicate-name auto-number policy reaches worker planning; skip policy blocks ambiguous names (no CAD export).");
            if (args.Length > 2 && !expectedBaselineFailure)
            {
                Call(form, "LoadProjectForScreenshot", Path.GetFullPath(args[2]), false, false);
                CheckBox actualDedup = (CheckBox)Field(form, "dedupCheck");
                ((CheckBox)Field(form, "assemblyCheck")).Checked = false;
                actualDedup.Checked = false;
                int raw = grid.Rows.Count;
                actualDedup.Checked = true;
                int folded = grid.Rows.Count;
                Assert(folded < raw, "Actual scan fixture must visibly fold duplicates.");
                actualDedup.Checked = false; Assert(grid.Rows.Count == raw, "Actual scan rows were lost.");
                actualDedup.Checked = true; Assert(grid.Rows.Count == folded, "Actual scan folding is not repeatable.");
                Console.WriteLine("PASS: actual scan data in real WinForms grid: " + raw + " -> " + folded + " -> " + raw + " -> " + folded + ". No live CAD export in this UI check.");
            }
            if (expectedBaselineFailure) throw new Exception("Expected baseline bug did not reproduce.");
            return 0;
        }
        catch (Exception ex)
        {
            Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            Console.WriteLine((expectedBaselineFailure ? "BASELINE REPRODUCED: " : "FAIL: ") + cause.Message);
            if (!expectedBaselineFailure) Console.WriteLine(cause.StackTrace);
            return expectedBaselineFailure && cause.Message.StartsWith("AUTOSAVE interrupted") ? 0 : 1;
        }
        finally { if (form != null) { Pump(500); Call(form, "AllowCloseForSelfTest"); form.Close(); form.Dispose(); } }
    }
}
