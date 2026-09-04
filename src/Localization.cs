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
            { "逐项整理", "Guided mode" }, { "批量分类", "Batch category" }, { "在 SW 中定位", "Locate in SW" },
            { "缩放", "Zoom" }, { "选", "Use" }, { "等轴测", "Isometric" }, { "前视图", "Front" }, { "上视图", "Top" },
            { "原实体名", "Original body" }, { "导出名称", "Export name" }, { "分类", "Category" }, { "相同件", "Qty" }, { "状态", "Status" },
            { "预览", "Preview" }, { "文件夹模板", "Folder template" }, { "应用", "Apply" }, { "另存模板", "Save template" },
            { "＋ 新建", "+ New" }, { "重命名", "Rename" }, { "删除", "Delete" }, { "目录树", "Folder tree" }, { "关系图", "Relationship map" },
            { "拖动目录调整父子关系；也可把实体拖到目录中。", "Drag folders to change hierarchy, or drag bodies onto a folder." },
            { "输出位置", "Output location" }, { "选择…", "Browse…" }, { "导出格式", "Export formats" }, { "Excel 报表", "Excel report" },
            { "原位装配体", "In-place assembly" }, { "导出规则", "Export rule" }, { "相同几何仅导出一件", "Export one per identical geometry" },
            { "STEP 存放", "STEP destination" },
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

        public LanguageDialog(bool startup)
        {
            UiBrand.ApplyIcon(this);
            Text = startup ? "选择界面语言 / Choose language" : UiText.T("语言设置", "Language settings");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = startup;
            ClientSize = new Size(420, 210);
            Font = new Font("Microsoft YaHei UI", 9F);
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
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public void ApplySelection()
        {
            if (UserSettingsStore.Current == null) UserSettingsStore.Load();
            UserSettingsStore.Current.Language = english.Checked ? "en-US" : "zh-CN";
            UserSettingsStore.Current.AskLanguageOnStartup = !remember.Checked;
            UserSettingsStore.Save();
        }
    }
}
