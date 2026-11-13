using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SWBodyOrganizer
{
    public sealed class WorkspaceShell : Form
    {
        private readonly IList<IWorkspaceModule> modules;
        private readonly Panel content = new Panel { Dock = DockStyle.Fill };
        private readonly Label caption = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private IWorkspaceModule active;
        private bool modulesDisposed;
        public WorkspaceShell() : this(ModuleRegistry.BuiltIn()) { }
        public WorkspaceShell(ModuleRegistry registry)
        {
            modules = registry.CreateModules();
            if (modules.Count == 0) throw new InvalidOperationException("At least one module is required.");
            Text = "喵师傅 Master Miao · " + UiBrand.VersionCaption;
            UiBrand.ApplyIcon(this);
            Font = UiBrand.CreateFont(9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(1280, 860);
            Size = new Size(1540, 960);
            StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            var brand = new Label { Text = "喵师傅", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = UiBrand.CreateFont(17F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(215, 25, 32), Image = Icon.ToBitmap(), ImageAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            var brandImage = brand.Image;
            brand.Disposed += (sender, e) => brandImage.Dispose();
            root.Controls.Add(brand, 0, 0);
            caption.Padding = new Padding(16, 0, 0, 0);
            caption.Font = UiBrand.CreateFont(12F, FontStyle.Bold);
            caption.BackColor = Color.White;
            root.Controls.Add(caption, 1, 0);
            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(239, 241, 244), Padding = new Padding(8, 16, 8, 0) };
            foreach (var module in modules)
            {
                var button = new Button { Text = module.Title, Width = 148, Height = 48, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Margin = new Padding(0, 0, 0, 8) };
                button.FlatAppearance.BorderSize = 0;
                string id = module.Id;
                button.Click += (sender, e) => ActivateModule(id);
                navigation.Add(id, button);
                nav.Controls.Add(button);
            }
            root.Controls.Add(nav, 0, 1);
            var assistant = new AssistantPane(() => ActiveModuleId, ActivateModule, () =>
            {
                var view = content.Controls.OfType<HoleAssistantView>().FirstOrDefault();
                view?.ReloadTags();
            }) { Dock = DockStyle.Right, Width = 330, Visible = false };
            var area = new Panel { Dock = DockStyle.Fill };
            area.Controls.Add(content);
            area.Controls.Add(assistant);
            root.Controls.Add(area, 1, 1);
            var ai = new Button { Text = "AI 助手", Width = 148, Height = 40 };
            ai.Click += (sender, args) => { assistant.Visible = !assistant.Visible; };
            nav.Controls.Add(ai);
            var settings = new Button { Text = UiText.T("设置","Settings"), Width = 148, Height = 40 };
            nav.Controls.Add(settings);
            settings.Click += (sender,e) =>
            {
                using (var dialog = new LanguageDialog(false))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    dialog.ApplySelection();
                }
                foreach (var form in content.Controls.OfType<MainForm>()) form.RefreshAfterSettings();
                foreach (var module in modules) navigation[module.Id].Text = module.Title;
                caption.Text = active?.Title;
                settings.Text = UiText.T("设置","Settings");
            };
            status.Padding = new Padding(12, 0, 0, 0);
            status.BackColor = Color.White;
            root.Controls.Add(status, 0, 2);
            root.SetColumnSpan(status, 2);
            Controls.Add(root);
            ActivateModule(modules[0].Id);
            FormClosing += OnClosing;
        }
        public string ActiveModuleId { get { return active == null ? null : active.Id; } }
        public bool ActivateModule(string id)
        {
            var next = modules.FirstOrDefault(module => module.Id == id);
            if (next == null) throw new ArgumentException("Unknown module: " + id);
            if (next == active) return true;
            if (modules.Any(module => module.IsBusy))
            {
                status.Text = UiText.T("当前任务正在运行，请完成或取消后切换模块。", "Complete or cancel the current task before switching modules.");
                return false;
            }
            Control view;
            try { view = next.View; }
            catch (Exception ex)
            {
                status.Text = UiText.T("模块加载失败：", "Module failed: ") + ex.Message;
                return false;
            }
            content.SuspendLayout();
            foreach (Control control in content.Controls) control.Hide();
            if (!content.Controls.Contains(view)) { view.Dock = DockStyle.Fill; content.Controls.Add(view); }
            view.Show();
            view.BringToFront();
            content.ResumeLayout();
            active = next;
            caption.Text = next.Title;
            status.Text = "Master Miao · " + UiBrand.VersionCaption;
            foreach (var pair in navigation)
            {
                pair.Value.BackColor = pair.Key == id ? Color.White : Color.FromArgb(239, 241, 244);
                pair.Value.ForeColor = pair.Key == id ? Color.FromArgb(190, 25, 32) : Color.FromArgb(45, 49, 55);
            }
            return true;
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (modules.Any(module => module.IsBusy))
            {
                e.Cancel = true;
                status.Text = UiText.T("请先在当前模块取消任务，等待安全结束后退出。", "Cancel the running task and wait for completion before closing.");
                return;
            }
            foreach (var module in modules) if (!module.CanClose()) { e.Cancel = true; return; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !modulesDisposed)
            {
                modulesDisposed = true;
                foreach (var module in modules) module.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
