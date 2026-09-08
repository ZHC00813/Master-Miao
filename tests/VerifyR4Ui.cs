using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SWBodyOrganizer;

// Layout/interaction tests only. The supplied project is loaded, never saved over.
internal static class VerifyR4Ui
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Field(object value, string name) { return value.GetType().GetField(name, Flags).GetValue(value); }
    private static object Call(object value, string name, params object[] args) { return value.GetType().GetMethod(name, Flags).Invoke(value, args); }
    private static void Assert(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static IEnumerable<Control> Descendants(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; } }
    private static void VisibleBounds(Control control)
    {
        Assert(control.Visible, "Hidden control: " + control.Text);
        Rectangle bounds = control.Parent.RectangleToScreen(control.Bounds);
        for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            Assert(parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds), "Clipped control: " + control.Text + " / " + control.GetType().Name + " in " + parent.GetType().Name);
    }
    private static void Shot(Form form, string path) { using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(path); } }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string fixture = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            byte[] original = File.ReadAllBytes(fixture); Directory.CreateDirectory(output);
            UserSettingsStore.Load(); typeof(MainForm).Assembly.GetType("SWBodyOrganizer.Program").GetField("SuppressStartupPrompts", Flags).SetValue(null, true);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            foreach (string language in new[] { "zh-CN", "en-US" })
            using (MainForm form = new MainForm())
            {
                UserSettingsStore.Current.Language = language;
                Call(form, "LoadProjectForScreenshot", fixture, false, false);
                Call(form, "UpdateEnvironmentSummary");
                form.StartPosition = FormStartPosition.Manual; form.Left = -30000; form.Show(); Application.DoEvents();
                DataGridView grid = (DataGridView)Field(form, "bodyGrid");
                AppProject project = (AppProject)Field(form, "project");
                Assert(grid.Columns["Selected"].Frozen, "Selection column is not frozen");
                foreach (Size size in new[] { new Size(1540, 920), new Size(1100, 720) })
                {
                    form.Size = size; Application.DoEvents();
                    foreach (string name in new[] { "sourceSearchBox", "bodySearch", "bodyFilter", "compactList", "guidedButton", "locateButton", "batchCategoryButton", "finishNameEditButton", "zoomCombo", "outputBox", "sldprtCheck", "stepCheck", "stepOnlyCheck", "assemblyCheck", "reportCheck", "dedupCheck", "stepFolderCombo", "conflictCombo", "cancelButton", "openReportButton", "exportButton" }) VisibleBounds((Control)Field(form, name));
                    foreach (Button button in Descendants(form).OfType<Button>().Where(button => button.Visible && (button.Text == "设置" || button.Text == "Settings" || button.Text == "侧栏" || button.Text == "Sidebar"))) VisibleBounds(button);
                    Assert(grid.DisplayedRowCount(true) >= 2, "Small layout does not expose a second row");
                    Shot(form, Path.Combine(output, language + "-" + size.Width + ".png"));
                }
                Button sidebar = Descendants(form).OfType<Button>().Single(button => button.Text == (language == "en-US" ? "Sidebar" : "侧栏"));
                int width = grid.Width; sidebar.PerformClick(); Application.DoEvents(); Assert(grid.Width > width + 200, "Sidebar did not release list space"); sidebar.PerformClick();
                Button selection = Descendants(form).OfType<Button>().Single(button => button.Text == (language == "en-US" ? "Selection ▾" : "选择 ▾"));
                selection.PerformClick(); ContextMenuStrip menu = selection.ContextMenuStrip;
                Assert(menu != null && menu.Visible, "Selection menu did not open");
                ((ToolStripMenuItem)menu.Items[1]).PerformClick(); menu.Close(); Application.DoEvents();
                Assert(project.AllBodies().All(body => !body.ExportSelected), "Select none menu lost original action");
                Button all = Descendants(form).OfType<Button>().Single(button => button.Text == (language == "en-US" ? "Select all" : "全选")); all.PerformClick();
                Assert(project.AllBodies().All(body => body.ExportSelected), "One-click Select all did not select bodies");
                Button more = Descendants(form).OfType<Button>().Single(button => button.Text == (language == "en-US" ? "More ▾" : "更多 ▾")); more.PerformClick(); menu = more.ContextMenuStrip;
                Assert(menu.Items.Count == 5 && !menu.Items[4].Enabled, "More menu lost a tool or enabled unavailable retry"); menu.Close(); Application.DoEvents();
                ((TabControl)Field(form, "rightTabs")).SelectedIndex = 1; Application.DoEvents();
                VisibleBounds((Control)Field(form, "templateCombo"));
                foreach (Button button in Descendants((Control)Field(form, "rightTabs")).OfType<Button>().Where(button => button.Visible)) VisibleBounds(button);
                Shot(form, Path.Combine(output, language + "-categories.png"));
                ((TabControl)Field(form, "categoryModes")).SelectedIndex = 1; Application.DoEvents(); Shot(form, Path.Combine(output, language + "-map.png"));
                Exception expandFailure = null; bool expanded = false;
                using (Timer timer = new Timer { Interval = 150 })
                {
                    timer.Tick += delegate
                    {
                        TabControl modes = (TabControl)Field(form, "categoryModes"); Form expandedForm = modes.FindForm();
                        if (expandedForm == form) return;
                        timer.Stop();
                        try { Assert(modes.Height > 500, "Expanded category workspace is too small"); Shot(expandedForm, Path.Combine(output, language + "-expanded-map.png")); expanded = true; }
                        catch (Exception ex) { expandFailure = ex; }
                        finally { expandedForm.Close(); }
                    };
                    timer.Start(); Call(form, "ExpandCategories", null, EventArgs.Empty);
                }
                if (expandFailure != null) throw expandFailure;
                Assert(expanded && ((Control)Field(form, "categoryModes")).FindForm() == form, "Category workspace did not return to sidebar");
                ((TabControl)Field(form, "rightTabs")).SelectedIndex = 0;
                ((CheckBox)Field(form, "compactList")).Checked = true;
                Assert(!grid.Columns["ThumbnailIso"].Visible && grid.Rows.Count > 0, "Compact mode changed record availability");
                Shot(form, Path.Combine(output, language + "-compact.png"));
                Call(form, "CaptureGuidedScreenshot", Path.Combine(output, language + "-guided.png"));
                Call(form, "AllowCloseForSelfTest"); form.Close();
                Console.WriteLine("PASS " + language + ": 1540/1100 control bounds, toolbar, footer, sidebar, selection menus, category tools, compact and guided views.");
            }
            Assert(original.SequenceEqual(File.ReadAllBytes(fixture)), "UI test changed the supplied project file");
            Console.WriteLine("PASS project fixture byte-identical. No SolidWorks connection or CAD operation."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
