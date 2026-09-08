using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SWBodyOrganizer
{
    internal static class UiText
    {
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "多实体零件 · 分类 · 去重 · 安全导出", "Multi-body parts · classify · deduplicate · safe export" },
            { "SolidWorks 多实体分类导出器", "SolidWorks Multi-body Organizer" },
            { "加载  →  整理  →  分类  →  导出", "Load  →  Organize  →  Classify  →  Export" },
            { "文件", "Files" }, { "＋ 添加零件", "+ Add parts" }, { "移除文件", "Remove file" },
            { "项目", "Project" }, { "打开项目", "Open project" }, { "保存项目", "Save project" },
            { "系统", "System" }, { "打开 SolidWorks", "Open SolidWorks" }, { "重新读取", "Rescan" }, { "设置", "Settings" },
            { "源文件", "Source files" }, { "搜索", "Search" }, { "多文件拖入 · 源文件只读", "Drop multiple files · sources stay read-only" },
            { "实体列表", "Body list" }, { "全选", "Select all" }, { "全不选", "Select none" }, { "反选", "Invert" },
            { "逐项整理", "Guided mode" }, { "批量分类", "Batch category" }, { "在 SW 中定位", "Locate in SW" }, { "命名完毕", "Finish naming" },
            { "缩放", "Zoom" }, { "选", "Use" }, { "等轴测", "Isometric" }, { "前视图", "Front" }, { "上视图", "Top" },
            { "原实体名", "Original body" }, { "导出名称", "Export name" }, { "分类", "Category" }, { "相同件", "Qty" }, { "状态", "Status" },
            { "预览", "Preview" }, { "文件夹模板", "Folder template" }, { "应用", "Apply" }, { "另存模板", "Save template" },
            { "＋ 新建", "+ New" }, { "重命名", "Rename" }, { "删除", "Delete" }, { "目录树", "Folder tree" }, { "关系图", "Relationship map" },
            { "拖动目录调整父子关系；也可把实体拖到目录中。", "Drag folders to change hierarchy, or drag bodies onto a folder." },
            { "输出位置", "Output location" }, { "选择…", "Browse…" }, { "导出格式", "Export formats" }, { "Excel 报表", "Excel report" },
            { "原位装配体", "In-place assembly" }, { "导出规则", "Export rule" }, { "相同几何仅导出一件", "Export one per identical geometry" },
            { "实体搜索", "Search bodies" }, { "全部状态", "All statuses" }, { "疑似重复", "Possible duplicates" },
            { "紧凑列表", "Compact list" }, { "撤销", "Undo" }, { "命名预览", "Batch names" },
            { "选择 ▾", "Selection ▾" }, { "更多 ▾", "More ▾" },
            { "安全说明", "Safety" },
            { "侧栏", "Sidebar" },
            { "展开", "Expand" },
            { "审查疑似重复", "Review duplicates" }, { "重试预览", "Retry previews" }, { "仅重试失败项", "Retry failures" },
            { "STEP 存放", "STEP destination" },
            { "仅导出 STEP", "STEP only" },
            { "上一项", "Previous issue" }, { "检查并下一项", "Check / next issue" }, { "收起", "Dismiss" },
            { "   导出规则", "   Export rule" },
            { "🛡  安全导出已启用  ·  只读源文件 · 隔离验证 · 覆盖备份", "Safe export enabled · read-only sources · isolated verification · overwrite backups" },
            { "取消任务", "Cancel task" }, { "打开目录", "Open folder" }, { "查看报表", "Open report" }, { "重名处理", "Name conflict" },
            { "跳过", "Skip" }, { "自动编号", "Auto-number" }, { "覆盖", "Overwrite" },
            { "请选择需要导出的实体", "Select bodies to export" },
            { "就绪。可拖入一个或多个 .SLDPRT 文件。", "Ready. Drop one or more .SLDPRT files." },
            { "将一个或多个 .SLDPRT 文件拖到这里", "Drop one or more .SLDPRT files here" },
            { "支持多文件选择，加入后自动读取实体并生成三视图", "Multiple files supported; bodies and three views are read automatically" }
        };

        public static bool IsEnglish
        {
            get { return UserSettingsStore.Current != null && string.Equals(UserSettingsStore.Current.Language, "en-US", StringComparison.OrdinalIgnoreCase); }
        }

        public static string T(string chinese, string english) { return IsEnglish ? english : chinese; }

        public static void Apply(Control root)
        {
            if (root == null) return;
            string translated;
            if (IsEnglish && English.TryGetValue(root.Text ?? string.Empty, out translated)) root.Text = translated;
            else if (!IsEnglish)
                foreach (KeyValuePair<string, string> item in English)
                    if (string.Equals(root.Text, item.Value, StringComparison.Ordinal)) { root.Text = item.Key; break; }
            foreach (Control child in root.Controls) Apply(child);
        }
    }

    internal static class UiBrand
    {
        public const string VersionCaption = "V1.2.6 · 0905-R4";
        public const string FontFamily = "Microsoft YaHei UI";
        public const float BaseFontSize = 10.5F;
        public const float SecondaryFontSize = 9.75F;

        public static Font CreateFont(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(FontFamily, size, style);
        }

        public static void StyleButtons(Control root)
        {
            Button button = root as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(208, 214, 222);
                button.FlatAppearance.MouseOverBackColor = button.BackColor.R > 180 && button.BackColor.G < 80
                    ? Color.FromArgb(190, 20, 29) : Color.FromArgb(255, 240, 241);
                button.MinimumSize = new Size(button.MinimumSize.Width, 32);
                button.Margin = new Padding(3, 0, 3, 0);
            }
            foreach (Control child in root.Controls) StyleButtons(child);
        }

        public static void ApplyIcon(Form form)
        {
            if (form == null) return;
            Icon extracted = null;
            try
            {
                extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                form.Icon = extracted == null ? (Icon)SystemIcons.Application.Clone() : (Icon)extracted.Clone();
            }
            catch { form.Icon = (Icon)SystemIcons.Application.Clone(); }
            finally { if (extracted != null) extracted.Dispose(); }
        }
    }

    internal sealed class LanguageDialog : Form
    {
        private readonly RadioButton chinese = new RadioButton();
        private readonly RadioButton english = new RadioButton();
        private readonly CheckBox remember = new CheckBox();
        private readonly ShortcutKeyBox locateKey = new ShortcutKeyBox();
        private readonly ShortcutKeyBox dedupKey = new ShortcutKeyBox();
        private readonly Label shortcutError = new Label();
        private readonly bool showShortcuts;

        public LanguageDialog(bool startup)
        {
            showShortcuts = !startup;
            UiBrand.ApplyIcon(this);
            Text = startup ? "选择界面语言 / Choose language" : UiText.T("语言设置", "Language settings");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = startup;
            ClientSize = new Size(420, 210);
            Font = UiBrand.CreateFont(UiBrand.BaseFontSize);
            BackColor = Color.White;

            Label title = new Label { Text = "界面语言 / Interface language", Left = 28, Top = 22, Width = 350, Height = 28, Font = new Font(Font, FontStyle.Bold) };
            chinese.Text = "中文"; chinese.Left = 32; chinese.Top = 65; chinese.Width = 120;
            english.Text = "English"; english.Left = 190; english.Top = 65; english.Width = 120;
            string current = UserSettingsStore.Current == null ? string.Empty : UserSettingsStore.Current.Language;
            english.Checked = string.Equals(current, "en-US", StringComparison.OrdinalIgnoreCase);
            chinese.Checked = !english.Checked;
            remember.Text = "下次不再询问 / Don't ask again";
            remember.Left = 32; remember.Top = 105; remember.Width = 310;
            remember.Checked = UserSettingsStore.Current != null && !UserSettingsStore.Current.AskLanguageOnStartup;

            Button ok = new Button { Text = "确定 / OK", DialogResult = DialogResult.OK, Left = 220, Top = 154, Width = 82, Height = 30 };
            Button cancel = new Button { Text = "取消 / Cancel", DialogResult = DialogResult.Cancel, Left = 310, Top = 154, Width = 82, Height = 30 };
            Controls.AddRange(new Control[] { title, chinese, english, remember, ok, cancel });
            if (showShortcuts)
            {
                Text = UiText.T("设置", "Settings");
                ClientSize = new Size(590, 390);
                ok.Text = UiText.T("确定", "OK"); cancel.Text = UiText.T("取消", "Cancel");
                Controls.Add(new Label { Text = UiText.T("快捷键（点击输入框，然后按下按键）", "Shortcuts (click a box, then press the keys)"), Left = 28, Top = 148, Width = 530, Height = 24, Font = new Font(Font, FontStyle.Bold) });
                AddShortcutRow(UiText.T("在 SW 中定位", "Locate in SW"), locateKey, 183, UserSettingsStore.Current.LocateShortcut);
                AddShortcutRow(UiText.T("切换相同件合并", "Toggle duplicate merging"), dedupKey, 225, UserSettingsStore.Current.DeduplicateShortcut);
                Controls.Add(new Label { Text = UiText.T("仅在本软件内生效；输入文字、读取和导出时暂停。默认不绑定。", "App-only; paused while typing, scanning or exporting. Unbound by default."), Left = 28, Top = 269, Width = 530, Height = 38 });
                shortcutError.SetBounds(28, 306, 530, 36); shortcutError.ForeColor = Color.Firebrick; Controls.Add(shortcutError);
                ok.Location = new Point(380, 350); cancel.Location = new Point(472, 350);
            }
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void AddShortcutRow(string text, ShortcutKeyBox box, int top, int key)
        {
            Controls.Add(new Label { Text = text, Left = 28, Top = top + 4, Width = 200, Height = 28 });
            box.SetBounds(235, top, 230, 29); box.Shortcut = (Keys)key; Controls.Add(box);
            Button clear = new Button { Text = UiText.T("清除", "Clear"), Left = 478, Top = top, Width = 80, Height = 29 };
            clear.Click += delegate { box.Shortcut = Keys.None; }; Controls.Add(clear);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (showShortcuts && DialogResult == DialogResult.OK)
            {
                string error = ShortcutBinding.Validate(locateKey.Shortcut, dedupKey.Shortcut);
                if (error.Length > 0) { shortcutError.Text = error; e.Cancel = true; DialogResult = DialogResult.None; }
            }
            base.OnFormClosing(e);
        }

        public void ApplySelection()
        {
            if (UserSettingsStore.Current == null) UserSettingsStore.Load();
            UserSettingsStore.Current.Language = english.Checked ? "en-US" : "zh-CN";
            UserSettingsStore.Current.AskLanguageOnStartup = !remember.Checked;
            if (showShortcuts)
            {
                UserSettingsStore.Current.LocateShortcut = (int)locateKey.Shortcut;
                UserSettingsStore.Current.DeduplicateShortcut = (int)dedupKey.Shortcut;
            }
            UserSettingsStore.Save();
        }
    }

    internal sealed class ShortcutKeyBox : TextBox
    {
        private Keys shortcut;
        internal Keys Shortcut
        {
            get { return shortcut; }
            set { shortcut = value; Text = value == Keys.None ? UiText.T("未设置", "Not assigned") : new KeysConverter().ConvertToString(value); }
        }
        internal ShortcutKeyBox() { ReadOnly = true; ShortcutsEnabled = false; BackColor = Color.White; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Tab || code == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            CaptureKey(keyData); return true;
        }
        protected override void OnKeyDown(KeyEventArgs e) { CaptureKey(e.KeyData); e.Handled = true; e.SuppressKeyPress = true; }
        private void CaptureKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu) return;
            Shortcut = code == Keys.Back ? Keys.None : keyData;
        }
    }

    // Application-local message filtering handles both command and letter keys without
    // registering global OS shortcuts or taking IME/text-editing keys away from controls.
    internal sealed class ShortcutBinding : IMessageFilter, IDisposable
    {
        private readonly Form owner;
        private readonly Func<bool> available;
        private readonly Action locate, toggle;
        internal ShortcutBinding(Form owner, Func<bool> available, Action locate, Action toggle)
        {
            this.owner = owner; this.available = available; this.locate = locate; this.toggle = toggle;
            Application.AddMessageFilter(this); owner.Disposed += delegate { Dispose(); };
        }
        internal static bool ValidKey(Keys value)
        {
            if (value == Keys.None) return true;
            Keys code = value & Keys.KeyCode;
            bool basic = (code >= Keys.A && code <= Keys.Z) || (code >= Keys.D0 && code <= Keys.D9) ||
                (code >= Keys.NumPad0 && code <= Keys.NumPad9) || (code >= Keys.F1 && code <= Keys.F24);
            if (!basic || (value & ~(Keys.KeyCode | Keys.Control | Keys.Alt | Keys.Shift)) != 0) return false;
            if ((value & Keys.Control) != 0 && "ACVXZYSOFNPR".IndexOf((char)code) >= 0) return false;
            return !((value & (Keys.Control | Keys.Alt)) != 0 && code == Keys.F4);
        }
        internal static string Validate(Keys first, Keys second)
        {
            if (!ValidKey(first) || !ValidKey(second)) return UiText.T("请使用字母、数字或功能键；不要使用关闭、复制、粘贴等保留快捷键。", "Use letters, numbers or function keys; close/copy/paste and other reserved shortcuts are unavailable.");
            if (first != Keys.None && first == second) return UiText.T("两个功能不能使用同一快捷键。", "The two actions cannot share a shortcut.");
            return string.Empty;
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != 0x100 && message.Msg != 0x104) return false;
            Control target = Control.FromChildHandle(message.HWnd);
            if (target == null || target.FindForm() != owner || !owner.Enabled || !available()) return false;
            Control focus = owner.ActiveControl;
            while (focus is ContainerControl && ((ContainerControl)focus).ActiveControl != null) focus = ((ContainerControl)focus).ActiveControl;
            if (focus is TextBoxBase || focus is ComboBox || (focus is DataGridView && ((DataGridView)focus).IsCurrentCellInEditMode)) return false;
            Keys key = (Keys)message.WParam.ToInt32() | Control.ModifierKeys;
            UserSettings settings = UserSettingsStore.Current;
            if (settings == null || !ValidKey(key)) return false;
            Action action = key != Keys.None && (int)key == settings.LocateShortcut ? locate :
                key != Keys.None && (int)key == settings.DeduplicateShortcut ? toggle : null;
            if (action == null) return false;
            if ((message.LParam.ToInt64() & 0x40000000) == 0) action();
            return true;
        }
        public void Dispose() { Application.RemoveMessageFilter(this); }
    }
}
