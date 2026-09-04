using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SWBodyOrganizer
{
    public sealed partial class MainForm : Form
    {
        private static readonly Color BrandRed = Color.FromArgb(215, 25, 32);
        private static readonly Color CanvasGray = Color.FromArgb(244, 246, 248);
        private AppProject project = new AppProject();
        private readonly ListBox sourceList = new ListBox();
        private readonly TextBox sourceSearchBox = new TextBox();
        private readonly DataGridView bodyGrid = new DataGridView();
        private readonly TabControl rightTabs = new TabControl();
        private readonly TabControl categoryModes = new TabControl();
        private readonly FolderCanvas folderCanvas = new FolderCanvas();
        private readonly TreeView categoryTree = new TreeView();
        private readonly Panel emptyStatePanel = new Panel();
        private readonly PictureBox previewFront = CreatePictureBox();
        private readonly PictureBox previewTop = CreatePictureBox();
        private readonly PictureBox previewIso = CreatePictureBox();
        private readonly Label previewNameLabel = new Label();
        private readonly Label previewDetailsLabel = new Label();
        private readonly ComboBox templateCombo = new ComboBox();
        private readonly TextBox outputBox = new TextBox();
        private readonly CheckBox sldprtCheck = new CheckBox();
        private readonly CheckBox stepCheck = new CheckBox();
        private readonly CheckBox reportCheck = new CheckBox();
        private readonly CheckBox assemblyCheck = new CheckBox();
        private readonly CheckBox dedupCheck = new CheckBox();
        private readonly ComboBox stepFolderCombo = new ComboBox();
        private readonly Label stepFolderHint = new Label();
        private readonly ComboBox conflictCombo = new ComboBox();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label progressLabel = new Label();
        private readonly Label environmentLabel = new Label();
        private readonly Label countLabel = new Label();
        private readonly Button cancelButton = new Button();
        private readonly Button exportButton = new Button();
        private readonly Button openReportButton = new Button();
        private readonly Button finishNameEditButton = new Button();
        private readonly TextBox exportNameEditor = new TextBox();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly BackgroundWorker worker = new BackgroundWorker();
        private string activeCancelFile = string.Empty;
        private Action<WorkerResponse> activeCompletion;
        private WorkerRequest activeRequest;
        private bool gridRefreshing;
        private bool categoryTreeRefreshing;
        private bool closeWhenIdle;
        private string lastReportPath = string.Empty;
        private string shownInterferenceMessage = string.Empty;
        private DateTime exportStartedAt;
        private int authorizedSolidWorksProcessId;
        private long authorizedSolidWorksStartTimeUtcTicks;
        private int activeAuthorizedSolidWorksProcessId;
        private long activeAuthorizedSolidWorksStartTimeUtcTicks;
        private bool activeTaskIsExport;
        private WorkerResponse lastDetection;
        private Point gridDragStart;
        private int exportNameEditRowIndex = -1;

        public MainForm()
        {
            AppPaths.Ensure();
            EnsureDefaultTemplate();
            project.OutputRoot = !string.IsNullOrWhiteSpace(UserSettingsStore.Current.LastOutputRoot)
                ? UserSettingsStore.Current.LastOutputRoot
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SW实体导出");
            Text = "Master Miao · V1.2.5";
            UiBrand.ApplyIcon(this);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 720);
            Size = new Size(1540, 920);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = CanvasGray;
            AllowDrop = true;
            InitializeLayout();
            InitializeGrid();
            InitializeWorker();
            BindProject();
            InitializeV120();
            UpdateEnvironmentSummary();
            DragEnter += MainDragEnter;
            DragDrop += MainDragDrop;
            FormClosing += HandleMainFormClosing;
        }

        internal void LoadProjectForScreenshot(string path, bool showClassification, bool showRelation)
        {
            project = JsonFile.Load<AppProject>(path);
            BindProject();
            if (showClassification && rightTabs.TabPages.Count > 1)
            {
                rightTabs.SelectedIndex = 1;
                categoryTree.ExpandAll();
                if (showRelation && categoryModes.TabPages.Count > 1) categoryModes.SelectedIndex = 1;
            }
        }

        private void InitializeLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0), BackColor = CanvasGray };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);

            Panel header = new Panel { Dock = DockStyle.Fill, BackColor = BrandRed, Padding = new Padding(18, 0, 18, 0) };
            PictureBox brandIcon = new PictureBox { Size = new Size(34, 34), Location = new Point(14, 6), SizeMode = PictureBoxSizeMode.Zoom, BackColor = BrandRed, Image = Icon.ToBitmap() };
            brandIcon.Disposed += delegate { if (brandIcon.Image != null) brandIcon.Image.Dispose(); };
            Label title = new Label { Text = "MASTER MIAO", ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold), AutoSize = true, Location = new Point(56, 8) };
            Label subtitle = new Label { Text = "SolidWorks 多实体分类导出器", ForeColor = Color.FromArgb(255, 226, 228), Font = new Font("Microsoft YaHei UI", 8.5F), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(1050, 14) };
            header.Controls.Add(brandIcon);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Resize += delegate { subtitle.Left = Math.Max(500, header.ClientSize.Width - subtitle.Width - 36); };
            root.Controls.Add(header, 0, 0);

            root.Controls.Add(BuildToolbar(), 0, 1);

            Panel workflow = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 250, 251), Padding = new Padding(18, 5, 18, 0) };
            workflow.Controls.Add(new Label { Text = "加载  →  整理  →  分类  →  导出", Dock = DockStyle.Left, AutoSize = true, ForeColor = Color.FromArgb(118, 123, 132), Font = new Font("Microsoft YaHei UI", 8F) });
            root.Controls.Add(workflow, 0, 2);

            SplitContainer outer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 230, SplitterWidth = 6, BackColor = CanvasGray, Padding = new Padding(8, 8, 8, 4) };
            root.Controls.Add(outer, 0, 3);
            outer.Panel1.Controls.Add(BuildSourcePanel());

            SplitContainer workArea = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 700, SplitterWidth = 6, FixedPanel = FixedPanel.Panel2, BackColor = CanvasGray };
            bool initialSplitSet = false;
            workArea.SizeChanged += delegate
            {
                if (initialSplitSet || workArea.ClientSize.Width < 600) return;
                int rightWidth = Math.Min(360, Math.Max(250, workArea.ClientSize.Width / 3));
                workArea.SplitterDistance = workArea.ClientSize.Width - rightWidth - workArea.SplitterWidth;
                initialSplitSet = true;
            };
            outer.Panel2.Controls.Add(workArea);
            workArea.Panel1.Controls.Add(BuildBodyPanel());
            workArea.Panel2.Controls.Add(BuildRightTabs());

            root.Controls.Add(BuildExportPanel(), 0, 4);
            Panel status = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10, 4, 10, 3) };
            progressLabel.Text = "就绪。可拖入一个或多个 .SLDPRT 文件。";
            progressLabel.AutoEllipsis = true;
            progressLabel.Dock = DockStyle.Fill;
            progressLabel.ForeColor = Color.FromArgb(70, 76, 86);
            status.Controls.Add(progressLabel);
            root.Controls.Add(status, 0, 5);
        }

        private Control BuildToolbar()
        {
            TableLayoutPanel bar = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 2, RowCount = 1, Padding = new Padding(10, 6, 10, 5) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            actions.Controls.Add(ToolbarCaption("文件"));
            actions.Controls.Add(MakeButton("＋ 添加零件", AddFiles));
            actions.Controls.Add(MakeButton("移除文件", RemoveSelectedSource));
            actions.Controls.Add(ToolbarDivider());
            actions.Controls.Add(ToolbarCaption("项目"));
            actions.Controls.Add(MakeButton("打开项目", OpenProject));
            actions.Controls.Add(MakeButton("保存项目", SaveProject));
            actions.Controls.Add(ToolbarDivider());
            actions.Controls.Add(ToolbarCaption("系统"));
            actions.Controls.Add(MakeButton("打开 SolidWorks", OpenSolidWorksManually));
            actions.Controls.Add(MakeButton("重新读取", delegate { StartScan(); }));
            actions.Controls.Add(MakeButton("设置", OpenSettings));
            bar.Controls.Add(actions, 0, 0);

            environmentLabel.AutoSize = true;
            environmentLabel.Cursor = Cursors.Hand;
            environmentLabel.ForeColor = Color.FromArgb(70, 76, 86);
            environmentLabel.Padding = new Padding(12, 8, 2, 0);
            environmentLabel.Click += DetectSolidWorks;
            toolTip.SetToolTip(environmentLabel, "点击检测 SolidWorks 版本、API、模板和装配体 STEP 导出入口");
            bar.Controls.Add(environmentLabel, 1, 0);
            return bar;
        }

        private Control BuildSourcePanel()
        {
            Panel panel = Card();
            Label title = SectionTitle("源文件");
            title.Dock = DockStyle.Top;
            Panel searchPanel = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(2, 4, 2, 5) };
            Label searchLabel = new Label { Text = "搜索", Dock = DockStyle.Left, Width = 43, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(92, 98, 108) };
            sourceSearchBox.Dock = DockStyle.Fill;
            sourceSearchBox.BorderStyle = BorderStyle.FixedSingle;
            sourceSearchBox.TextChanged += delegate { RefreshSources(); };
            toolTip.SetToolTip(sourceSearchBox, "输入文件名筛选；留空显示全部源文件");
            searchPanel.Controls.Add(sourceSearchBox);
            searchPanel.Controls.Add(searchLabel);
            sourceList.Dock = DockStyle.Fill;
            sourceList.BorderStyle = BorderStyle.None;
            sourceList.IntegralHeight = false;
            sourceList.DrawMode = DrawMode.OwnerDrawFixed;
            sourceList.ItemHeight = 50;
            sourceList.DrawItem += DrawSourceItem;
            sourceList.SelectedIndexChanged += delegate { CommitExportNameEdit(); RefreshGrid(); };
            sourceList.MouseMove += ShowSourceToolTip;
            Label hint = new Label { Text = "多文件拖入 · 源文件只读", Dock = DockStyle.Bottom, Height = 31, ForeColor = Color.FromArgb(105, 111, 122), Padding = new Padding(3, 7, 0, 0) };
            panel.Controls.Add(sourceList);
            panel.Controls.Add(hint);
            panel.Controls.Add(searchPanel);
            panel.Controls.Add(title);
            return panel;
        }

        private Control BuildBodyPanel()
        {
            Panel panel = Card();
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.White };
            Label title = SectionTitle("实体列表");
            title.Location = new Point(4, 8);
            countLabel.AutoSize = true;
            countLabel.Location = new Point(112, 13);
            countLabel.ForeColor = Color.FromArgb(95, 101, 111);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
            guidedButton.Text = "逐项整理";
            guidedButton.AutoSize = true;
            guidedButton.Height = 27;
            guidedButton.Click += OpenGuidedMode;
            locateButton.Text = "在 SW 中定位";
            locateButton.AutoSize = true;
            locateButton.Height = 27;
            locateButton.Click += LocateSelectedBodies;
            batchCategoryButton.Text = "批量分类";
            batchCategoryButton.AutoSize = true;
            batchCategoryButton.Height = 27;
            batchCategoryButton.Click += BatchCategorize;
            actions.Controls.Add(guidedButton);
            actions.Controls.Add(locateButton);
            actions.Controls.Add(batchCategoryButton);
            finishNameEditButton.Text = "命名完毕";
            finishNameEditButton.AutoSize = true;
            finishNameEditButton.Height = 28;
            finishNameEditButton.BackColor = Color.White;
            finishNameEditButton.ForeColor = BrandRed;
            finishNameEditButton.FlatStyle = FlatStyle.Flat;
            finishNameEditButton.FlatAppearance.BorderColor = BrandRed;
            finishNameEditButton.Margin = new Padding(2, 0, 2, 0);
            finishNameEditButton.Enabled = false;
            finishNameEditButton.CausesValidation = false;
            finishNameEditButton.Click += FinishExportNameEdit;
            toolTip.SetToolTip(finishNameEditButton, "双击导出名称开始输入；输入法选字和 Enter 不会退出，点击这里提交，Esc 取消");
            actions.Controls.Add(finishNameEditButton);
            actions.Controls.Add(MakeSmallButton("全选", delegate { SetSelection(1); }));
            actions.Controls.Add(MakeSmallButton("全不选", delegate { SetSelection(0); }));
            actions.Controls.Add(MakeSmallButton("反选", delegate { SetSelection(-1); }));
            actions.Controls.Add(new Label { Text = "缩放", AutoSize = true, Padding = new Padding(8, 5, 2, 0), ForeColor = Color.FromArgb(95, 101, 111) });
            zoomCombo.Width = 72;
            zoomCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            zoomCombo.Items.AddRange(new object[] { "80%", "100%", "125%", "150%", "175%", "200%" });
            zoomCombo.SelectedIndexChanged += ZoomChanged;
            actions.Controls.Add(zoomCombo);
            bar.Controls.Add(actions);
            bar.Controls.Add(countLabel);
            bar.Controls.Add(title);
            bodyGrid.Dock = DockStyle.Fill;
            panel.Controls.Add(bodyGrid);
            BuildEmptyState();
            panel.Controls.Add(emptyStatePanel);
            panel.Controls.Add(bar);
            return panel;
        }

        private void BuildEmptyState()
        {
            emptyStatePanel.Dock = DockStyle.Fill;
            emptyStatePanel.BackColor = Color.White;
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.White };
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            Label icon = new Label { Text = "▱", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(190, 195, 202), Font = new Font("Segoe UI Symbol", 30F) };
            Label primary = new Label { Text = "将一个或多个 .SLDPRT 文件拖到这里", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(52, 58, 66), Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) };
            Button add = MakeButton("＋ 添加零件", AddFiles);
            add.Anchor = AnchorStyles.None;
            add.BackColor = BrandRed;
            add.ForeColor = Color.White;
            add.FlatAppearance.BorderSize = 0;
            content.Controls.Add(new Panel(), 0, 0);
            content.Controls.Add(icon, 0, 1);
            content.Controls.Add(primary, 0, 2);
            content.Controls.Add(add, 0, 3);
            content.Controls.Add(new Label { Text = "支持多文件选择，加入后自动读取实体并生成三视图", Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.FromArgb(115, 121, 130) }, 0, 4);
            emptyStatePanel.Controls.Add(content);
        }

        private Control BuildRightTabs()
        {
            rightTabs.Dock = DockStyle.Fill;
            rightTabs.Font = Font;
            TabPage previewPage = new TabPage("预览") { BackColor = Color.White, Padding = new Padding(7) };
            Panel info = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.White, Padding = new Padding(6, 5, 6, 5) };
            previewNameLabel.Dock = DockStyle.Top;
            previewNameLabel.Height = 28;
            previewNameLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            previewNameLabel.ForeColor = Color.FromArgb(45, 50, 58);
            previewDetailsLabel.Dock = DockStyle.Fill;
            previewDetailsLabel.ForeColor = Color.FromArgb(100, 106, 116);
            previewDetailsLabel.AutoEllipsis = true;
            info.Controls.Add(previewDetailsLabel);
            info.Controls.Add(previewNameLabel);
            TabControl viewTabs = new TabControl { Dock = DockStyle.Fill };
            viewTabs.TabPages.Add(PreviewTab("等轴测", previewIso));
            viewTabs.TabPages.Add(PreviewTab("前视图", previewFront));
            viewTabs.TabPages.Add(PreviewTab("上视图", previewTop));
            previewPage.Controls.Add(viewTabs);
            previewPage.Controls.Add(info);

            TabPage folderPage = new TabPage("分类") { BackColor = Color.White, Padding = new Padding(7) };
            FlowLayoutPanel templateBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 39, WrapContents = false };
            templateBar.Controls.Add(new Label { Text = "文件夹模板", AutoSize = true, Padding = new Padding(0, 6, 5, 0), ForeColor = Color.FromArgb(75, 81, 90) });
            templateCombo.Width = 138;
            templateCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            templateBar.Controls.Add(templateCombo);
            templateBar.Controls.Add(MakeSmallButton("应用", ApplySelectedTemplate));
            templateBar.Controls.Add(MakeSmallButton("另存模板", SaveTemplateAs));
            FlowLayoutPanel editBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
            editBar.Controls.Add(MakeSmallButton("＋ 新建", AddCategory));
            editBar.Controls.Add(MakeSmallButton("重命名", RenameCategory));
            editBar.Controls.Add(MakeSmallButton("删除", DeleteCategory));
            Label dragHint = new Label { Text = "拖动目录调整父子关系；也可把实体拖到目录中。", Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.FromArgb(95, 101, 111), Padding = new Padding(2, 4, 0, 0) };
            categoryModes.Dock = DockStyle.Fill;
            TabPage treePage = new TabPage("目录树") { BackColor = Color.White, Padding = new Padding(4) };
            categoryTree.Dock = DockStyle.Fill;
            categoryTree.BorderStyle = BorderStyle.None;
            categoryTree.ShowNodeToolTips = true;
            categoryTree.HideSelection = false;
            categoryTree.AllowDrop = true;
            categoryTree.AfterSelect += CategoryTreeAfterSelect;
            categoryTree.ItemDrag += CategoryTreeItemDrag;
            categoryTree.DragEnter += CategoryTreeDragEnter;
            categoryTree.DragOver += CategoryTreeDragEnter;
            categoryTree.DragDrop += CategoryTreeDragDrop;
            treePage.Controls.Add(categoryTree);
            TabPage visualPage = new TabPage("关系图") { BackColor = Color.White, Padding = new Padding(4) };
            folderCanvas.Dock = DockStyle.Fill;
            folderCanvas.TreeChanged += delegate { CategoryTreeChanged(); };
            visualPage.Controls.Add(folderCanvas);
            categoryModes.TabPages.Add(treePage);
            categoryModes.TabPages.Add(visualPage);
            folderPage.Controls.Add(categoryModes);
            folderPage.Controls.Add(dragHint);
            folderPage.Controls.Add(editBar);
            folderPage.Controls.Add(templateBar);
            rightTabs.TabPages.Add(previewPage);
            rightTabs.TabPages.Add(folderPage);
            return rightTabs;
        }

        private Control BuildExportPanel()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 7, 12, 7) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 455));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

            Panel output = new Panel { Dock = DockStyle.Fill };
            Label outputLabel = new Label { Text = "输出位置", AutoSize = true, Location = new Point(0, 9), ForeColor = Color.FromArgb(55, 61, 70) };
            outputBox.Location = new Point(108, 5);
            outputBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            outputBox.Width = 555;
            outputBox.TextChanged += delegate { project.OutputRoot = outputBox.Text.Trim(); if (!suppressDirty) { UserSettingsStore.Current.LastOutputRoot = project.OutputRoot; MarkProjectDirty(); } };
            Button browse = MakeSmallButton("选择…", ChooseOutput);
            browse.Location = new Point(660, 3);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            output.Resize += delegate { outputBox.Width = Math.Max(180, output.ClientSize.Width - 188); browse.Left = output.ClientSize.Width - 72; };
            output.Controls.Add(outputLabel);
            output.Controls.Add(outputBox);
            output.Controls.Add(browse);
            layout.Controls.Add(output, 0, 0);

            Label safety = new Label { Text = "🛡  安全导出已启用  ·  只读源文件 · 隔离验证 · 覆盖备份", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(70, 112, 82), Cursor = Cursors.Hand, AutoEllipsis = true };
            safety.Click += ShowSafetyDetails;
            layout.Controls.Add(safety, 1, 0);

            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0), WrapContents = false };
            options.Controls.Add(new Label { Text = "导出格式", AutoSize = true, Padding = new Padding(0, 3, 4, 0), ForeColor = Color.FromArgb(75, 81, 90) });
            sldprtCheck.Text = "SLDPRT"; sldprtCheck.AutoSize = true;
            stepCheck.Text = "STEP"; stepCheck.AutoSize = true;
            reportCheck.Text = "Excel 报表"; reportCheck.AutoSize = true;
            assemblyCheck.Text = "原位装配体"; assemblyCheck.AutoSize = true;
            dedupCheck.Text = "相同几何仅导出一件"; dedupCheck.AutoSize = true;
            options.Controls.Add(sldprtCheck);
            options.Controls.Add(stepCheck);
            options.Controls.Add(reportCheck);
            options.Controls.Add(assemblyCheck);
            options.Controls.Add(new Label { Text = "   导出规则", AutoSize = true, Padding = new Padding(4, 3, 4, 0), ForeColor = Color.FromArgb(75, 81, 90) });
            options.Controls.Add(dedupCheck);
            layout.SetColumnSpan(options, 2);
            layout.Controls.Add(options, 0, 1);
            sldprtCheck.CheckedChanged += delegate { if (!suppressDirty) MarkProjectDirty(); UpdateSelectionSummary(); };
            stepCheck.CheckedChanged += delegate { if (stepCheck.Checked) sldprtCheck.Checked = true; if (!suppressDirty) MarkProjectDirty(); UpdateSelectionSummary(); };
            reportCheck.CheckedChanged += delegate { if (!suppressDirty) MarkProjectDirty(); UpdateSelectionSummary(); };
            assemblyCheck.CheckedChanged += delegate { if (assemblyCheck.Checked) sldprtCheck.Checked = true; if (!suppressDirty) MarkProjectDirty(); UpdateSelectionSummary(); };
            dedupCheck.CheckedChanged += delegate { if (!gridRefreshing && !suppressDirty) { project.Export.Deduplicate = dedupCheck.Checked; MarkProjectDirty(); RefreshGrid(); } else UpdateSelectionSummary(); };

            FlowLayoutPanel stepFolders = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 3, 0, 0), WrapContents = false };
            stepFolders.Controls.Add(new Label { Text = "STEP 存放", AutoSize = true, Padding = new Padding(0, 6, 4, 0), ForeColor = Color.FromArgb(75, 81, 90) });
            stepFolderCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            stepFolderCombo.Width = 250;
            stepFolderCombo.Items.AddRange(new object[] { "与 SLDPRT 同目录", "独立双目录（镜像分类树）" });
            stepFolderCombo.SelectedIndexChanged += delegate
            {
                if (stepFolderCombo.SelectedIndex < 0) return;
                project.Export.SeparateStepOutput = stepFolderCombo.SelectedIndex == 1;
                UpdateStepFolderHint();
                if (!suppressDirty) MarkProjectDirty();
            };
            stepFolders.Controls.Add(stepFolderCombo);
            stepFolderHint.AutoSize = true;
            stepFolderHint.Padding = new Padding(8, 6, 0, 0);
            stepFolderHint.ForeColor = Color.FromArgb(104, 110, 120);
            stepFolders.Controls.Add(stepFolderHint);
            layout.SetColumnSpan(stepFolders, 2);
            layout.Controls.Add(stepFolders, 0, 2);

            exportButton.Text = "请选择需要导出的实体";
            exportButton.Dock = DockStyle.Fill;
            exportButton.BackColor = BrandRed;
            exportButton.ForeColor = Color.White;
            exportButton.FlatStyle = FlatStyle.Flat;
            exportButton.FlatAppearance.BorderSize = 0;
            exportButton.Font = new Font(Font, FontStyle.Bold);
            exportButton.Click += delegate { StartExport(); };
            layout.SetRowSpan(exportButton, 4);
            layout.Controls.Add(exportButton, 2, 0);

            Panel progressPanel = new Panel { Dock = DockStyle.Fill };
            progress.Dock = DockStyle.Fill;
            progress.Style = ProgressBarStyle.Continuous;
            progress.Minimum = 0;
            progress.Maximum = 100;
            progressPanel.Padding = new Padding(0, 12, 0, 10);
            progressPanel.Controls.Add(progress);
            layout.Controls.Add(progressPanel, 0, 3);

            FlowLayoutPanel policy = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
            policy.Controls.Add(new Label { Text = "重名处理", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
            conflictCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            conflictCombo.Width = 112;
            conflictCombo.Items.AddRange(new object[] { UiText.T("跳过", "Skip"), UiText.T("自动编号", "Auto-number"), UiText.T("覆盖", "Overwrite") });
            conflictCombo.SelectedIndexChanged += delegate { if (!suppressDirty) MarkProjectDirty(); };
            policy.Controls.Add(conflictCombo);
            cancelButton.Text = "取消任务";
            cancelButton.Width = 82;
            cancelButton.Height = 27;
            cancelButton.Enabled = false;
            cancelButton.Click += delegate { RequestCancel(); };
            policy.Controls.Add(cancelButton);
            Button openOutput = MakeSmallButton("打开目录", OpenOutputFolder);
            policy.Controls.Add(openOutput);
            openReportButton.Text = "查看报表";
            openReportButton.AutoSize = true;
            openReportButton.Height = 28;
            openReportButton.BackColor = Color.White;
            openReportButton.FlatStyle = FlatStyle.Flat;
            openReportButton.FlatAppearance.BorderColor = Color.FromArgb(211, 215, 221);
            openReportButton.Enabled = false;
            openReportButton.Click += OpenLastReport;
            policy.Controls.Add(openReportButton);
            layout.Controls.Add(policy, 1, 3);
            panel.Controls.Add(layout);
            return panel;
        }

        private void InitializeGrid()
        {
            bodyGrid.AllowUserToAddRows = false;
            bodyGrid.AllowUserToDeleteRows = false;
            bodyGrid.AllowUserToResizeRows = false;
            bodyGrid.BackgroundColor = Color.White;
            bodyGrid.BorderStyle = BorderStyle.None;
            bodyGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            bodyGrid.GridColor = Color.FromArgb(231, 234, 238);
            bodyGrid.RowHeadersVisible = false;
            bodyGrid.RowTemplate.Height = 76;
            bodyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            bodyGrid.MultiSelect = true;
            bodyGrid.AutoGenerateColumns = false;
            bodyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            bodyGrid.EnableHeadersVisualStyles = false;
            bodyGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);
            bodyGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(52, 58, 66);
            bodyGrid.ColumnHeadersHeight = 34;
            bodyGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "选", Width = 42, MinimumWidth = 42, FillWeight = 32 });
            bodyGrid.Columns.Add(new DataGridViewImageColumn { Name = "ThumbnailIso", HeaderText = "等轴测", Width = 92, MinimumWidth = 70, ReadOnly = true, ImageLayout = DataGridViewImageCellLayout.Zoom });
            bodyGrid.Columns.Add(new DataGridViewImageColumn { Name = "ThumbnailFront", HeaderText = "前视图", Width = 92, MinimumWidth = 70, ReadOnly = true, ImageLayout = DataGridViewImageCellLayout.Zoom });
            bodyGrid.Columns.Add(new DataGridViewImageColumn { Name = "ThumbnailTop", HeaderText = "上视图", Width = 92, MinimumWidth = 70, ReadOnly = true, ImageLayout = DataGridViewImageCellLayout.Zoom });
            bodyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OriginalName", HeaderText = "原实体名", Width = 112, MinimumWidth = 100, ReadOnly = true });
            bodyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExportName", HeaderText = "导出名称", Width = 142, MinimumWidth = 125, ReadOnly = true });
            bodyGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Category", HeaderText = "分类", Width = 160, MinimumWidth = 125, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton });
            bodyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "相同件", Width = 68, MinimumWidth = 60, FillWeight = 52, ReadOnly = true });
            bodyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", Width = 82, MinimumWidth = 76, FillWeight = 62, ReadOnly = true });
            bodyGrid.CurrentCellDirtyStateChanged += GridCurrentCellDirtyStateChanged;
            bodyGrid.CellValueChanged += GridCellValueChanged;
            bodyGrid.CellDoubleClick += BeginExportNameEdit;
            bodyGrid.CellMouseDown += GridCellMouseDown;
            bodyGrid.SelectionChanged += delegate { ShowSelectedPreviews(); };
            bodyGrid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            bodyGrid.MouseDown += delegate(object sender, MouseEventArgs e) { gridDragStart = e.Location; };
            bodyGrid.MouseMove += BodyGridMouseMove;
            bodyGrid.MouseWheel += BodyGridMouseWheel;
            bodyGrid.Scroll += delegate { PositionExportNameEditor(); };
            bodyGrid.ColumnWidthChanged += delegate { PositionExportNameEditor(); };
            bodyGrid.RowHeightChanged += delegate { PositionExportNameEditor(); };
            bodyGrid.SizeChanged += delegate { PositionExportNameEditor(); };

            exportNameEditor.Visible = false;
            exportNameEditor.BorderStyle = BorderStyle.FixedSingle;
            exportNameEditor.BackColor = Color.White;
            exportNameEditor.ForeColor = Color.FromArgb(32, 36, 42);
            exportNameEditor.Font = Font;
            exportNameEditor.HideSelection = false;
            exportNameEditor.KeyDown += ExportNameEditorKeyDown;
            bodyGrid.Controls.Add(exportNameEditor);
        }

        private void InitializeWorker()
        {
            worker.WorkerReportsProgress = true;
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                WorkerJob job = (WorkerJob)e.Argument;
                StringBuilder errors = new StringBuilder();
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--worker " + Quote(job.RequestPath) + " " + Quote(job.ResponsePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                using (Process process = Process.Start(info))
                {
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        string[] parts = line.Split(new[] { '\t' }, 4);
                        int value;
                        if (parts.Length == 4 && parts[0] == "PROGRESS" && int.TryParse(parts[1], out value))
                            worker.ReportProgress(Math.Max(0, Math.Min(100, value)), new WorkerProgress { Stage = FromBase64(parts[2]), Detail = FromBase64(parts[3]) });
                    }
                    errors.Append(process.StandardError.ReadToEnd());
                    process.WaitForExit();
                }
                WorkerResponse response = File.Exists(job.ResponsePath) ? JsonFile.Load<WorkerResponse>(job.ResponsePath) : new WorkerResponse { Success = false, Message = "工作进程没有生成结果。" };
                if (errors.Length > 0 && string.IsNullOrWhiteSpace(response.Message)) response.Message = errors.ToString();
                e.Result = response;
            };
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e)
            {
                progress.Value = Math.Max(progress.Minimum, Math.Min(progress.Maximum, e.ProgressPercentage));
                WorkerProgress detail = e.UserState as WorkerProgress;
                if (detail != null)
                {
                    progressLabel.Text = LocalWorkerStage(detail.Stage) + "｜" + detail.Detail;
                    if (detail.Stage == "检测到 SolidWorks 干扰" && !string.Equals(shownInterferenceMessage, detail.Detail, StringComparison.Ordinal))
                    {
                        shownInterferenceMessage = detail.Detail;
                        MessageBox.Show(this, detail.Detail + "\n\n请先停止操作 SolidWorks，再根据导出结果决定是否重试。",
                            "SolidWorks 操作受到干扰", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    if (detail.Stage == "导出零件")
                    {
                        string counter = detail.Detail.Split(new[] { '：' }, 2)[0];
                        exportButton.Text = UiText.T("正在导出 ", "Exporting ") + counter;
                    }
                    else if (detail.Stage == "生成装配体") exportButton.Text = UiText.T("正在生成装配体", "Creating assembly");
                    else exportButton.Text = UiText.IsEnglish ? LocalWorkerStage(detail.Stage) + "…" : "正在" + detail.Stage + "…";
                }
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                bool completedExportTask = activeTaskIsExport;
                int completedAuthorizedProcessId = activeAuthorizedSolidWorksProcessId;
                long completedAuthorizedStartTimeUtcTicks = activeAuthorizedSolidWorksStartTimeUtcTicks;
                WorkerRequest completedRequest = activeRequest;
                Action<WorkerResponse> completedCompletion = activeCompletion;
                activeTaskIsExport = false;
                activeRequest = null;
                activeCompletion = null;
                activeAuthorizedSolidWorksProcessId = 0;
                activeAuthorizedSolidWorksStartTimeUtcTicks = 0;
                SetBusy(false);
                if (e.Error != null)
                {
                    CloseAuthorizedSolidWorks(completedAuthorizedProcessId, completedAuthorizedStartTimeUtcTicks);
                    progressLabel.Text = "任务失败：" + e.Error.Message;
                    string errorMessage = completedExportTask
                        ? "导出工作进程异常结束。\n\n本次工作耗时：" + FormatElapsed(DateTime.Now - exportStartedAt) +
                          "\n本次成功导出文件：0 个\n\n失败原因：\n• " + e.Error.Message +
                          "\n\n辛苦了，愿灵感的火花永不熄灭"
                        : e.Error.Message;
                    MessageBox.Show(this, errorMessage, "任务失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                WorkerResponse response = e.Result as WorkerResponse ?? new WorkerResponse { Message = "没有结果。" };
                if (!response.SolidWorksKeptOpen)
                    CloseAuthorizedSolidWorks(completedAuthorizedProcessId, completedAuthorizedStartTimeUtcTicks);
                if (completedAuthorizedProcessId > 0 && completedRequest != null &&
                    string.Equals(completedRequest.Operation, "scan", StringComparison.OrdinalIgnoreCase) &&
                    IsAutomaticSolidWorksConnectionFailure(response) && WaitForManuallyStartedSolidWorks())
                {
                    completedRequest.StagingRoot = string.Empty;
                    StartWorker(completedRequest, completedCompletion);
                    return;
                }
                progressLabel.Text = response.Message;
                if (completedCompletion != null) completedCompletion(response);
                if (closeWhenIdle) BeginInvoke(new Action(Close));
            };
        }

        private void AddFiles(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "SolidWorks 零件 (*.SLDPRT)|*.SLDPRT", Multiselect = true, Title = "选择一个或多个多实体零件" })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddSourcePaths(dialog.FileNames);
        }

        private void AddSourcePaths(IEnumerable<string> paths)
        {
            if (worker.IsBusy) { MessageBox.Show(this, "请等待当前任务结束。", "任务进行中"); return; }
            int added = 0;
            foreach (string raw in paths)
            {
                string path;
                try { path = Path.GetFullPath(raw); } catch { continue; }
                if (!string.Equals(Path.GetExtension(path), ".SLDPRT", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                if (project.Sources.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                project.Sources.Add(new SourceRecord { Path = path, Name = Path.GetFileName(path), Status = "待读取" });
                added++;
            }
            RefreshSources();
            if (added > 0) { MarkProjectDirty(); StartScan(); }
        }

        private void StartScan()
        {
            if (worker.IsBusy || project.Sources.Count == 0) return;
            if (!ConfirmSolidWorksTask(UiText.T("读取", "scan"), UiText.T("读取实体并生成三视图", "read bodies and generate three projections"), true)) return;
            CommitGrid();
            Dictionary<string, BodyRecord> oldBodies = project.AllBodies().ToDictionary(item => item.SourceId + "|" + item.Index, item => item);
            WorkerRequest request = new WorkerRequest
            {
                Operation = "scan",
                CacheRoot = AppPaths.Cache,
                GeneratePreviews = true,
                KeepSourceDocumentsOpen = true,
                Sources = project.Sources.Select(item => new SourceRecord { Id = item.Id, Path = item.Path, Name = item.Name }).ToList()
            };
            StartWorker(request, delegate(WorkerResponse response)
            {
                if (response.Sources != null && response.Sources.Count > 0)
                {
                    foreach (SourceRecord source in response.Sources)
                        foreach (BodyRecord body in source.Bodies ?? new List<BodyRecord>())
                        {
                            BodyRecord old;
                            if (oldBodies.TryGetValue(body.SourceId + "|" + body.Index, out old) && (string.IsNullOrWhiteSpace(old.GeometryKey) || old.GeometryKey == body.GeometryKey))
                            {
                                body.ExportName = old.ExportName;
                                body.CategoryId = project.Categories.Any(item => item.Id == old.CategoryId) ? old.CategoryId : CategoryNode.UnclassifiedId;
                                body.ExportSelected = old.ExportSelected;
                            }
                        }
                    project.Sources = response.Sources;
                    MarkProjectDirty();
                    RefreshSources();
                    RefreshGrid();
                }
                if (!response.Success) MessageBox.Show(this, response.Message, "读取未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        private void DetectSolidWorks(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            if (!ConfirmSolidWorksTask(UiText.T("检测", "test"), UiText.T("检测 SolidWorks 版本、模板和自动导出能力", "check the SolidWorks version, templates, and automatic export capability"), false)) return;
            WorkerRequest request = new WorkerRequest { Operation = "detect", GeneratePreviews = false };
            StartWorker(request, delegate(WorkerResponse response)
            {
                lastDetection = response;
                if (!response.Success)
                {
                    environmentLabel.Text = "● SolidWorks 连接异常";
                    environmentLabel.ForeColor = Color.Firebrick;
                }
                else if (!response.StepAvailable)
                {
                    environmentLabel.Text = "● SolidWorks " + response.SolidWorksRevision + " 已连接 · STEP 入口异常";
                    environmentLabel.ForeColor = Color.FromArgb(191, 106, 25);
                }
                else
                {
                    environmentLabel.Text = "● SolidWorks " + response.SolidWorksRevision + " 已连接";
                    environmentLabel.ForeColor = Color.FromArgb(44, 125, 75);
                }
                string details = response.Message +
                    (string.IsNullOrWhiteSpace(response.SolidWorksRevision) ? string.Empty : "\n\n版本 / API：" + response.SolidWorksRevision + " / 已连接") +
                    "\n零件模板：" + (string.IsNullOrWhiteSpace(response.TemplatePath) ? "不可用" : response.TemplatePath) +
                    "\n装配体模板：" + (string.IsNullOrWhiteSpace(response.AssemblyTemplatePath) ? "不可用" : response.AssemblyTemplatePath) +
                    "\nSTEP 导出入口：" + (response.StepAvailable ? "可用" : "不可用") +
                    (string.IsNullOrWhiteSpace(response.StepDiagnostic) ? string.Empty : "\n" + response.StepDiagnostic);
                MessageBox.Show(this, details, "SolidWorks 连接状态", MessageBoxButtons.OK, response.Success && response.StepAvailable ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            });
        }

        private void StartExport()
        {
            if (worker.IsBusy) return;
            CommitGrid();
            project.OutputRoot = outputBox.Text.Trim();
            project.Export.ExportSldprt = sldprtCheck.Checked;
            project.Export.ExportStep = stepCheck.Checked;
            project.Export.SeparateStepOutput = stepFolderCombo.SelectedIndex == 1;
            project.Export.CreateExcel = reportCheck.Checked;
            project.Export.CreateAssembly = assemblyCheck.Checked;
            project.Export.Deduplicate = dedupCheck.Checked;
            project.Export.ConflictPolicy = conflictCombo.SelectedIndex == 1 ? "自动编号" : conflictCombo.SelectedIndex == 2 ? "覆盖" : "跳过";

            string validation;
            List<ExportPlanItem> plans = BuildExportPlan(out validation);
            if (!string.IsNullOrWhiteSpace(validation))
            {
                MessageBox.Show(this, validation, "导出前检查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ConfirmSolidWorksTask(UiText.T("导出", "export"), UiText.T("拆分零件、生成装配体和 STEP", "split bodies, create an assembly, and generate STEP files"), false)) return;
            exportStartedAt = DateTime.Now;
            WorkerRequest request = new WorkerRequest
            {
                Operation = "export",
                ExportItems = plans,
                ExportSettings = JsonFile.Clone(project.Export),
                OutputRoot = project.OutputRoot
            };
            StartWorker(request, delegate(WorkerResponse response)
            {
                foreach (ExportResultItem item in response.ExportResults ?? new List<ExportResultItem>())
                {
                    BodyRecord body = project.AllBodies().FirstOrDefault(value => value.Id == item.BodyId);
                    if (body != null)
                    {
                        if (item.StepStatus == "失败") body.Status = "STEP 导出失败";
                        else if (item.AssemblyStatus == "失败") body.Status = "装配体生成失败";
                        else body.Status = item.VerificationStatus;
                        body.Message = item.Message;
                    }
                }
                RefreshGrid();
                string reportPath = string.Empty;
                string reportFailure = string.Empty;
                if (project.Export.CreateExcel && response.ExportResults != null && response.ExportResults.Count > 0)
                {
                    try
                    {
                        reportPath = Path.Combine(project.OutputRoot, "实体导出清单_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                        ExcelReportWriter.Create(reportPath, response.ExportResults, project.Name, project.OutputRoot, UserSettingsStore.Current.Language);
                        lastReportPath = reportPath;
                        openReportButton.Enabled = File.Exists(lastReportPath);
                    }
                    catch (Exception reportError) { reportFailure = "Excel 报表生成失败：" + reportError.Message; }
                }
                TimeSpan elapsed = DateTime.Now - exportStartedAt;
                HashSet<string> exportedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ExportResultItem item in response.ExportResults ?? new List<ExportResultItem>())
                {
                    AddSuccessfulFile(exportedFiles, item.SldprtPath, item.SldprtStatus);
                    AddSuccessfulFile(exportedFiles, item.StepPath, item.StepStatus);
                }
                foreach (AssemblyResultItem item in response.AssemblyResults ?? new List<AssemblyResultItem>())
                {
                    AddSuccessfulFile(exportedFiles, item.AssemblyPath, item.Status);
                    AddSuccessfulFile(exportedFiles, item.AssemblyStepPath, item.StepStatus);
                }
                if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath)) exportedFiles.Add(Path.GetFullPath(reportPath));
                bool completedSuccessfully = response.Success && string.IsNullOrWhiteSpace(reportFailure);
                project.LastExportSucceeded = completedSuccessfully;
                project.LastExportUtc = DateTime.UtcNow;
                projectDirty = true;
                SaveProjectSilently();
                string failureDetails = response.Success ? string.Empty : BuildFailureDetails(response);
                if (!string.IsNullOrWhiteSpace(reportFailure)) failureDetails += "\n\n失败原因：\n• " + reportFailure;
                string message = response.Message +
                    "\n\n本次工作耗时：" + FormatElapsed(elapsed) +
                    "\n本次成功导出文件：" + exportedFiles.Count + " 个" +
                    failureDetails +
                    (string.IsNullOrWhiteSpace(reportPath) ? string.Empty : "\n报表：" + reportPath) +
                    "\n\n辛苦了，愿灵感的火花永不熄灭";
                MessageBox.Show(this, message, completedSuccessfully ? "导出完成" : "导出结束", MessageBoxButtons.OK, completedSuccessfully ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            });
        }

        private static void AddSuccessfulFile(HashSet<string> files, string path, string status)
        {
            if (!string.Equals(status, "成功", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            files.Add(Path.GetFullPath(path));
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
        }

        private static string BuildFailureDetails(WorkerResponse response)
        {
            List<string> reasons = new List<string>();
            foreach (ExportResultItem item in response.ExportResults ?? new List<ExportResultItem>())
                if ((item.SldprtStatus == "失败" || item.StepStatus == "失败" || item.AssemblyStatus == "失败") && !string.IsNullOrWhiteSpace(item.Message))
                    reasons.Add(item.Message.Trim());
            foreach (AssemblyResultItem item in response.AssemblyResults ?? new List<AssemblyResultItem>())
                if ((item.Status == "失败" || item.StepStatus == "失败") && !string.IsNullOrWhiteSpace(item.Message))
                    reasons.Add(item.Message.Trim());
            reasons = reasons.Distinct(StringComparer.CurrentCulture).Take(8).ToList();
            if (reasons.Count == 0 && !string.IsNullOrWhiteSpace(response.Message)) reasons.Add(response.Message.Trim());
            if (reasons.Count == 0) reasons.Add("工作进程未返回可识别的失败原因，请查看任务目录中的响应记录。");
            return "\n\n失败原因：\n• " + string.Join("\n• ", reasons.ToArray());
        }

        private List<ExportPlanItem> BuildExportPlan(out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(project.OutputRoot)) { error = "请选择主输出文件夹。"; return new List<ExportPlanItem>(); }
            if (!ValidateOutputRoot(project.OutputRoot, out error)) return new List<ExportPlanItem>();
            if (!project.Export.ExportSldprt && !project.Export.ExportStep) { error = "请至少选择 SLDPRT 或 STEP 一种格式。"; return new List<ExportPlanItem>(); }
            if (project.Export.ExportStep && !project.Export.ExportSldprt) { error = "STEP 使用装配体批量导出，需要同时勾选 SLDPRT。"; return new List<ExportPlanItem>(); }
            if (project.Export.CreateAssembly && !project.Export.ExportSldprt) { error = "生成原位装配体时必须同时导出 SLDPRT。"; return new List<ExportPlanItem>(); }
            if (project.Export.CreateAssembly && project.Export.Deduplicate) { error = "原位装配体需要保留每个实体的原始位置，当前版本不能同时启用“相同几何仅导出一件”。请关闭去重后再生成装配体。"; return new List<ExportPlanItem>(); }
            List<BodyRecord> selected = project.AllBodies().Where(item => item.ExportSelected).ToList();
            if (selected.Count == 0) { error = "没有选中要导出的实体。"; return new List<ExportPlanItem>(); }
            foreach (SourceRecord source in project.Sources)
            {
                FileInfo info = new FileInfo(source.Path);
                if (!info.Exists) { error = "源文件不存在：\n" + source.Path; return new List<ExportPlanItem>(); }
                if (source.Length > 0 && (info.Length != source.Length || info.LastWriteTimeUtc.Ticks != source.LastWriteTicks))
                { error = "源文件在读取后发生了变化，请重新读取：\n" + source.Path; return new List<ExportPlanItem>(); }
            }
            foreach (BodyRecord body in selected)
                if (string.IsNullOrWhiteSpace(body.ExportName)) { error = "实体“" + body.OriginalName + "”的导出名称为空。"; return new List<ExportPlanItem>(); }

            List<List<BodyRecord>> groups = new List<List<BodyRecord>>();
            if (project.Export.Deduplicate)
            {
                foreach (IGrouping<string, BodyRecord> group in selected.GroupBy(item => string.IsNullOrWhiteSpace(item.GeometryKey) ? item.Id : item.GeometryKey))
                {
                    List<BodyRecord> values = group.ToList();
                    if (values.Select(item => item.CategoryId).Distinct().Count() > 1)
                    { error = "相同几何实体被分到了不同文件夹。启用去重前，请给同组实体设置相同标签：\n" + string.Join("、", values.Select(item => item.ExportName).ToArray()); return new List<ExportPlanItem>(); }
                    groups.Add(values);
                }
            }
            else foreach (BodyRecord item in selected) groups.Add(new List<BodyRecord> { item });

            List<ExportPlanItem> plans = new List<ExportPlanItem>();
            foreach (List<BodyRecord> group in groups)
            {
                BodyRecord body = group[0];
                plans.Add(new ExportPlanItem
                {
                    BodyId = body.Id,
                    SourcePath = body.SourcePath,
                    SourceName = body.SourceName,
                    BodyIndex = body.Index,
                    OriginalName = body.OriginalName,
                    ExportName = NameRules.SafeStem(body.ExportName, "零件"),
                    CategoryPath = CategoryRules.GetPath(project.Categories, body.CategoryId),
                    PreviewFront = body.PreviewFront,
                    PreviewTop = body.PreviewTop,
                    PreviewIso = body.PreviewIso,
                    GeometryKey = body.GeometryKey,
                    Quantity = group.Count,
                    Occurrences = group.Select(item => item.SourceName + " / " + item.OriginalName).ToList()
                });
            }
            var repeated = plans.GroupBy(item => item.CategoryPath + "|" + item.ExportName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(item => item.Count() > 1);
            if (repeated != null) { error = "同一文件夹内存在重复导出名称：“" + repeated.First().ExportName + "”。请先重命名。"; return new List<ExportPlanItem>(); }
            if (project.Export.CreateAssembly || project.Export.ExportStep)
            {
                var repeatedFileName = plans.GroupBy(item => item.ExportName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(item => item.Count() > 1);
                if (repeatedFileName != null) { error = "装配体与批量 STEP 要求所有已选零件的文件名唯一：“" + repeatedFileName.Key + "”出现了多次。请先重命名。"; return new List<ExportPlanItem>(); }
            }
            return plans;
        }

        private void StartWorker(WorkerRequest request, Action<WorkerResponse> completion)
        {
            int launchProcessId = authorizedSolidWorksProcessId;
            long launchStartTimeUtcTicks = authorizedSolidWorksStartTimeUtcTicks;
            try
            {
                string id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string jobFolder = Path.Combine(AppPaths.Jobs, id);
                Directory.CreateDirectory(jobFolder);
                string requestPath = Path.Combine(jobFolder, "request.json");
                string responsePath = Path.Combine(jobFolder, "response.json");
                activeCancelFile = Path.Combine(jobFolder, "cancel.request");
                request.CancelFile = activeCancelFile;
                if (string.IsNullOrWhiteSpace(request.StagingRoot)) request.StagingRoot = Path.Combine(jobFolder, "staging");
                request.AuthorizedSolidWorksProcessId = launchProcessId;
                request.AuthorizedSolidWorksStartTimeUtcTicks = launchStartTimeUtcTicks;
                JsonFile.Save(requestPath, request);
                activeAuthorizedSolidWorksProcessId = launchProcessId;
                activeAuthorizedSolidWorksStartTimeUtcTicks = launchStartTimeUtcTicks;
                authorizedSolidWorksProcessId = 0;
                authorizedSolidWorksStartTimeUtcTicks = 0;
                activeCompletion = completion;
                activeRequest = request;
                shownInterferenceMessage = string.Empty;
                progress.Value = 0;
                activeTaskIsExport = string.Equals(request.Operation, "export", StringComparison.OrdinalIgnoreCase);
                SetBusy(true);
                worker.RunWorkerAsync(new WorkerJob { RequestPath = requestPath, ResponsePath = responsePath });
            }
            catch (Exception ex)
            {
                CloseAuthorizedSolidWorks(launchProcessId, launchStartTimeUtcTicks);
                authorizedSolidWorksProcessId = 0;
                authorizedSolidWorksStartTimeUtcTicks = 0;
                activeAuthorizedSolidWorksProcessId = 0;
                activeAuthorizedSolidWorksStartTimeUtcTicks = 0;
                activeRequest = null;
                activeCompletion = null;
                activeTaskIsExport = false;
                SetBusy(false);
                bool exportTask = string.Equals(request.Operation, "export", StringComparison.OrdinalIgnoreCase);
                string errorMessage = exportTask
                    ? "导出工作进程未能启动。\n\n本次工作耗时：" + FormatElapsed(DateTime.Now - exportStartedAt) +
                      "\n本次成功导出文件：0 个\n\n失败原因：\n• " + ex.Message +
                      "\n\n辛苦了，愿灵感的火花永不熄灭"
                    : "工作进程未能启动：\n" + ex.Message;
                MessageBox.Show(this, errorMessage, "任务失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void CloseAuthorizedSolidWorks(int processId, long expectedStartTimeUtcTicks)
        {
            if (processId <= 0 || expectedStartTimeUtcTicks <= 0) return;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (process.StartTime.ToUniversalTime().Ticks != expectedStartTimeUtcTicks) return;
                    if (!process.HasExited && process.CloseMainWindow()) process.WaitForExit(3000);
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
            }
            catch (ArgumentException) { }
            catch { }
        }

        private void RequestCancel()
        {
            if (!worker.IsBusy || string.IsNullOrWhiteSpace(activeCancelFile)) return;
            try { File.WriteAllText(activeCancelFile, "cancel"); progressLabel.Text = "已发送取消请求，正在安全结束当前步骤…"; cancelButton.Enabled = false; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法取消", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void SetBusy(bool busy)
        {
            cancelButton.Enabled = busy;
            guidedButton.Enabled = !busy;
            locateButton.Enabled = !busy;
            batchCategoryButton.Enabled = !busy;
            bodyGrid.Enabled = !busy;
            UseWaitCursor = busy;
            if (busy)
            {
                exportButton.Enabled = false;
                exportButton.Text = UiText.T("正在处理…", "Processing…");
            }
            else UpdateSelectionSummary();
        }

        private void BindProject()
        {
            suppressDirty = true;
            if (project.Categories == null || project.Categories.Count == 0) project.Categories = CategoryNode.CreateDefaultTree();
            if (project.Sources == null) project.Sources = new List<SourceRecord>();
            if (project.Export == null) project.Export = new ExportSettings();
            outputBox.Text = project.OutputRoot;
            sldprtCheck.Checked = project.Export.ExportSldprt;
            stepCheck.Checked = project.Export.ExportStep;
            stepFolderCombo.SelectedIndex = project.Export.SeparateStepOutput ? 1 : 0;
            reportCheck.Checked = project.Export.CreateExcel;
            assemblyCheck.Checked = project.Export.CreateAssembly;
            dedupCheck.Checked = project.Export.Deduplicate;
            conflictCombo.SelectedIndex = project.Export.ConflictPolicy == "自动编号" ? 1 : project.Export.ConflictPolicy == "覆盖" ? 2 : 0;
            folderCanvas.Nodes = project.Categories;
            RefreshCategoryTree();
            RefreshTemplateList();
            RefreshSources();
            RefreshGrid();
            UpdateSelectionSummary();
            ApplyListZoom(project.ListZoomPercent < 80 ? UserSettingsStore.Current.ListZoomPercent : project.ListZoomPercent);
            ApplyLanguage();
            UpdateStepFolderHint();
            suppressDirty = false;
        }

        private void UpdateStepFolderHint()
        {
            bool separate = stepFolderCombo.SelectedIndex == 1;
            stepFolderHint.Text = separate
                ? UiText.T("主输出\\零件源文件 与 主输出\\STEP生产文件采用相同分类树", "Mirrored trees under Part source files and STEP production files")
                : UiText.T("SLDPRT 与 STEP 保存在各自分配的同一分类文件夹", "SLDPRT and STEP share each assigned category folder");
            toolTip.SetToolTip(stepFolderCombo, separate
                ? UiText.T("零件源文件与生产 STEP 完全分开，但分类父子结构保持一致。", "Separates source parts from production STEP files while preserving the same category tree.")
                : UiText.T("每个 STEP 与对应 SLDPRT 保存在同一个分类文件夹。", "Stores every STEP beside its matching SLDPRT file."));
        }

        private void RefreshSources()
        {
            string selectedId = (sourceList.SelectedItem as SourceListItem) == null ? string.Empty : ((SourceListItem)sourceList.SelectedItem).Id;
            string search = sourceSearchBox.Text.Trim();
            sourceList.BeginUpdate();
            sourceList.Items.Clear();
            sourceList.Items.Add(new SourceListItem { Id = string.Empty, Name = UiText.T("全部文件", "All files"), Summary = string.Format(UiText.T("{0} 个文件 · {1} 个实体", "{0} files · {1} bodies"), project.Sources.Count, project.AllBodies().Count()), Status = string.Empty, Path = string.Empty });
            foreach (SourceRecord source in project.Sources.Where(item => string.IsNullOrWhiteSpace(search) || item.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0))
                sourceList.Items.Add(new SourceListItem
                {
                    Id = source.Id,
                    Name = source.Name,
                    Summary = (source.BodyCount > 0 ? string.Format(UiText.T("{0} 个实体", "{0} bodies"), source.BodyCount) : UiText.T("待读取", "Pending")) + " · " + ShortSourceStatus(source.Status),
                    Status = source.Status,
                    Path = source.Path
                });
            sourceList.EndUpdate();
            for (int i = 0; i < sourceList.Items.Count; i++) if (((SourceListItem)sourceList.Items[i]).Id == selectedId) { sourceList.SelectedIndex = i; break; }
            if (sourceList.SelectedIndex < 0 && sourceList.Items.Count > 0) sourceList.SelectedIndex = 0;
        }

        private void RefreshGrid()
        {
            gridRefreshing = true;
            string filter = (sourceList.SelectedItem as SourceListItem) == null ? string.Empty : ((SourceListItem)sourceList.SelectedItem).Id;
            List<CategoryOption> categories = CategoryRules.BuildOptions(project.Categories);
            DataGridViewComboBoxColumn categoryColumn = (DataGridViewComboBoxColumn)bodyGrid.Columns["Category"];
            categoryColumn.DataSource = categories;
            categoryColumn.DisplayMember = "Path";
            categoryColumn.ValueMember = "Id";
            DisposeGridImages();
            bodyGrid.Rows.Clear();
            List<BodyRecord> visibleBodies = GetDisplayBodies(filter);
            foreach (BodyRecord body in visibleBodies)
            {
                Image iso = LoadImage(body.PreviewIso);
                Image front = LoadImage(body.PreviewFront);
                Image top = LoadImage(body.PreviewTop);
                int quantity = GetGroupMembers(body).Count;
                int row = bodyGrid.Rows.Add(body.ExportSelected, iso, front, top, body.OriginalName, body.ExportName, body.CategoryId, quantity > 1 ? "×" + quantity : "1", ShortBodyStatus(body));
                bodyGrid.Rows[row].Tag = body;
                foreach (DataGridViewCell cell in bodyGrid.Rows[row].Cells) cell.ToolTipText = body.SourceName + (string.IsNullOrWhiteSpace(body.Message) ? string.Empty : "\n" + body.Message);
                if (body.Status.Contains("失败")) bodyGrid.Rows[row].DefaultCellStyle.ForeColor = Color.Firebrick;
                else if (body.CategoryId == CategoryNode.UnclassifiedId) bodyGrid.Rows[row].Cells["Status"].Style.ForeColor = Color.FromArgb(202, 115, 25);
                else bodyGrid.Rows[row].Cells["Status"].Style.ForeColor = Color.FromArgb(44, 125, 75);
            }
            int rawCount = string.IsNullOrWhiteSpace(filter) ? project.AllBodies().Count() : project.AllBodies().Count(item => item.SourceId == filter);
            countLabel.Text = project.Export.Deduplicate
                ? string.Format(UiText.T("{0} 组 / {1} 个实体 · 已选 {2}", "{0} groups / {1} bodies · selected {2}"), visibleBodies.Count, rawCount, visibleBodies.Count(item => item.ExportSelected))
                : string.Format(UiText.T("{0} 个实体 · 已选 {1}", "{0} bodies · selected {1}"), visibleBodies.Count, visibleBodies.Count(item => item.ExportSelected));
            emptyStatePanel.Visible = project.Sources.Count == 0;
            gridRefreshing = false;
            ShowSelectedPreviews();
            RefreshCategoryTree();
            UpdateSelectionSummary();
        }

        private void CommitGrid()
        {
            CommitExportNameEdit();
            if (bodyGrid.IsCurrentCellInEditMode) bodyGrid.EndEdit();
            foreach (DataGridViewRow row in bodyGrid.Rows)
            {
                BodyRecord body = row.Tag as BodyRecord;
                if (body == null) continue;
                ApplyToGroup(body,
                    Convert.ToString(row.Cells["ExportName"].Value) ?? string.Empty,
                    Convert.ToString(row.Cells["Category"].Value) ?? CategoryNode.UnclassifiedId,
                    Convert.ToBoolean(row.Cells["Selected"].Value ?? false));
            }
        }

        private void GridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (gridRefreshing || e.RowIndex < 0) return;
            if (e.ColumnIndex == bodyGrid.Columns["ExportName"].Index) return;
            BodyRecord body = bodyGrid.Rows[e.RowIndex].Tag as BodyRecord;
            if (body == null) return;
            bool selected = Convert.ToBoolean(bodyGrid.Rows[e.RowIndex].Cells["Selected"].Value ?? false);
            string exportName = Convert.ToString(bodyGrid.Rows[e.RowIndex].Cells["ExportName"].Value) ?? string.Empty;
            string categoryId = Convert.ToString(bodyGrid.Rows[e.RowIndex].Cells["Category"].Value) ?? CategoryNode.UnclassifiedId;
            ApplyToGroup(body, exportName, categoryId, selected);
            MarkProjectDirty();
            UpdateSelectionSummary();
        }

        private static bool ShouldCommitDirtyCell(DataGridViewCell cell)
        {
            return cell is DataGridViewCheckBoxCell || cell is DataGridViewComboBoxCell;
        }

        private void GridCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (!bodyGrid.IsCurrentCellDirty || !ShouldCommitDirtyCell(bodyGrid.CurrentCell)) return;
            bodyGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void BeginExportNameEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != bodyGrid.Columns["ExportName"].Index) return;
            if (exportNameEditor.Visible && exportNameEditRowIndex != e.RowIndex) CommitExportNameEdit();
            bodyGrid.CurrentCell = bodyGrid.Rows[e.RowIndex].Cells["ExportName"];
            exportNameEditRowIndex = e.RowIndex;
            exportNameEditor.Text = Convert.ToString(bodyGrid.CurrentCell.Value) ?? string.Empty;
            PositionExportNameEditor();
            exportNameEditor.Visible = true;
            exportNameEditor.BringToFront();
            finishNameEditButton.Enabled = true;
            exportNameEditor.Focus();
            exportNameEditor.SelectAll();
        }

        private void GridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (!exportNameEditor.Visible || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.RowIndex == exportNameEditRowIndex && e.ColumnIndex == bodyGrid.Columns["ExportName"].Index) return;
            CommitExportNameEdit();
        }

        private void FinishExportNameEdit(object sender, EventArgs e)
        {
            CommitExportNameEdit();
            bodyGrid.Focus();
        }

        private void CommitExportNameEdit()
        {
            if (!exportNameEditor.Visible) return;
            int rowIndex = exportNameEditRowIndex;
            string enteredName = exportNameEditor.Text;
            exportNameEditor.Visible = false;
            exportNameEditRowIndex = -1;
            finishNameEditButton.Enabled = false;
            if (rowIndex < 0 || rowIndex >= bodyGrid.Rows.Count) return;
            DataGridViewRow row = bodyGrid.Rows[rowIndex];
            BodyRecord body = row.Tag as BodyRecord;
            if (body == null) return;
            body.ExportName = NameRules.SafeStem(enteredName, "零件_" + (body.Index + 1));
            row.Cells["ExportName"].Value = body.ExportName;
            ApplyToGroup(body, body.ExportName, body.CategoryId, body.ExportSelected);
            MarkProjectDirty();
            ShowSelectedPreviews();
        }

        private void CancelExportNameEdit()
        {
            if (!exportNameEditor.Visible) return;
            exportNameEditor.Visible = false;
            exportNameEditRowIndex = -1;
            finishNameEditButton.Enabled = false;
            bodyGrid.Focus();
        }

        private void ExportNameEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelExportNameEdit();
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void PositionExportNameEditor()
        {
            if (exportNameEditRowIndex < 0 || exportNameEditRowIndex >= bodyGrid.Rows.Count || bodyGrid.Columns["ExportName"] == null) return;
            Rectangle cellBounds = bodyGrid.GetCellDisplayRectangle(bodyGrid.Columns["ExportName"].Index, exportNameEditRowIndex, true);
            if (cellBounds.Width < 8 || cellBounds.Height < 8) return;
            int height = Math.Min(cellBounds.Height - 4, exportNameEditor.PreferredHeight);
            exportNameEditor.SetBounds(cellBounds.X + 2, cellBounds.Y + Math.Max(2, (cellBounds.Height - height) / 2), Math.Max(20, cellBounds.Width - 4), height);
            if (exportNameEditor.Visible) exportNameEditor.BringToFront();
        }

        private void SetSelection(int mode)
        {
            foreach (DataGridViewRow row in bodyGrid.Rows)
            {
                BodyRecord body = row.Tag as BodyRecord;
                if (body == null) continue;
                bool next = mode > 0 || (mode < 0 && !body.ExportSelected);
                foreach (BodyRecord member in GetGroupMembers(body)) member.ExportSelected = next;
                row.Cells["Selected"].Value = body.ExportSelected;
            }
            MarkProjectDirty();
            UpdateSelectionSummary();
        }

        private void ShowSelectedPreviews()
        {
            BodyRecord body = bodyGrid.CurrentRow == null ? null : bodyGrid.CurrentRow.Tag as BodyRecord;
            SetPicture(previewFront, body == null ? string.Empty : body.PreviewFront);
            SetPicture(previewTop, body == null ? string.Empty : body.PreviewTop);
            SetPicture(previewIso, body == null ? string.Empty : body.PreviewIso);
            previewNameLabel.Text = body == null ? UiText.T("未选择实体", "No body selected") : body.ExportName;
            previewDetailsLabel.Text = body == null
                ? UiText.T("从实体列表选择一项以查看三视图与分类信息。", "Select a row to view three projections and category details.")
                : string.Format(UiText.T("原实体：{0}\n来源：{1} · 分类：{2} · 相同件：{3}", "Original: {0}\nSource: {1} · Category: {2} · Identical: {3}"), body.OriginalName, body.SourceName, DisplayCategoryPath(body.CategoryId), GetGroupMembers(body).Count);
        }

        private void AddCategory(object sender, EventArgs e)
        {
            CategoryNode selectedNode = SelectedCategoryNode();
            string parentId = selectedNode == null || selectedNode.Id == CategoryNode.UnclassifiedId ? CategoryNode.RootId : selectedNode.Id;
            using (CategoryDialog dialog = new CategoryDialog(project.Categories, parentId, "新建标签 / 文件夹", string.Empty))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (project.Categories.Any(item => item.ParentId == dialog.ParentId && string.Equals(item.Name, dialog.CategoryName, StringComparison.CurrentCultureIgnoreCase)))
                { MessageBox.Show(this, "同一父文件夹下已经有同名分类。", "分类", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                CategoryNode node = new CategoryNode { Name = dialog.CategoryName, ParentId = dialog.ParentId, Order = project.Categories.Count, ColorHex = "#D71920" };
                project.Categories.Add(node);
                folderCanvas.Nodes = project.Categories;
                folderCanvas.SelectedId = node.Id;
                CategoryTreeChanged();
                SelectCategoryTreeNode(node.Id);
            }
        }

        private void RenameCategory(object sender, EventArgs e)
        {
            CategoryNode node = SelectedCategoryNode();
            if (node == null || node.IsSystem) { MessageBox.Show(this, "系统分类不能重命名。", "分类"); return; }
            using (TextPrompt dialog = new TextPrompt("重命名标签 / 文件夹", "新名称", node.Name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string nextName = NameRules.SafeStem(dialog.Value, "分类");
                if (project.Categories.Any(item => item.Id != node.Id && item.ParentId == node.ParentId && string.Equals(item.Name, nextName, StringComparison.CurrentCultureIgnoreCase)))
                { MessageBox.Show(this, "同一父文件夹下已经有同名分类。", "分类", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                node.Name = nextName;
                folderCanvas.Nodes = project.Categories;
                CategoryTreeChanged();
            }
        }

        private void DeleteCategory(object sender, EventArgs e)
        {
            CategoryNode node = SelectedCategoryNode();
            if (node == null || node.IsSystem) { MessageBox.Show(this, "系统分类不能删除。", "分类"); return; }
            if (MessageBox.Show(this, "删除“" + node.Name + "”？它的下级会移到上一级，已归类实体会转到“未分类”。", "删除分类", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (CategoryNode child in project.Categories.Where(item => item.ParentId == node.Id)) child.ParentId = node.ParentId;
            foreach (BodyRecord body in project.AllBodies().Where(item => item.CategoryId == node.Id)) body.CategoryId = CategoryNode.UnclassifiedId;
            project.Categories.Remove(node);
            folderCanvas.Nodes = project.Categories;
            folderCanvas.SelectedId = node.ParentId;
            CategoryTreeChanged();
            SelectCategoryTreeNode(node.ParentId);
        }

        private void CategoryTreeChanged()
        {
            project.TemplateName = "自定义（未保存）";
            MarkProjectDirty();
            RefreshCategoryTree();
            RefreshGrid();
        }

        private void EnsureDefaultTemplate()
        {
            string path = Path.Combine(AppPaths.Templates, "默认模板.json");
            if (!File.Exists(path)) JsonFile.Save(path, new FolderTemplate());
        }

        private void RefreshTemplateList()
        {
            string preferred = project.TemplateName;
            templateCombo.Items.Clear();
            foreach (string file in Directory.GetFiles(AppPaths.Templates, "*.json").OrderBy(item => item)) templateCombo.Items.Add(new TemplateItem { Name = Path.GetFileNameWithoutExtension(file), Path = file });
            for (int i = 0; i < templateCombo.Items.Count; i++) if (((TemplateItem)templateCombo.Items[i]).Name == preferred) { templateCombo.SelectedIndex = i; break; }
            if (templateCombo.SelectedIndex < 0 && templateCombo.Items.Count > 0) templateCombo.SelectedIndex = 0;
        }

        private void ApplySelectedTemplate(object sender, EventArgs e)
        {
            TemplateItem selected = templateCombo.SelectedItem as TemplateItem;
            if (selected == null) return;
            try
            {
                FolderTemplate template = JsonFile.Load<FolderTemplate>(selected.Path);
                List<CategoryNode> old = project.Categories;
                List<CategoryNode> next = JsonFile.Clone(template.Categories);
                foreach (BodyRecord body in project.AllBodies())
                {
                    string oldPath = CategoryRules.GetPath(old, body.CategoryId);
                    CategoryNode match = next.FirstOrDefault(item => string.Equals(CategoryRules.GetPath(next, item.Id), oldPath, StringComparison.CurrentCultureIgnoreCase));
                    body.CategoryId = match == null ? CategoryNode.UnclassifiedId : match.Id;
                }
                project.Categories = next;
                project.TemplateName = template.Name;
                MarkProjectDirty();
                folderCanvas.Nodes = project.Categories;
                RefreshCategoryTree();
                RefreshGrid();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "模板无法应用", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveTemplateAs(object sender, EventArgs e)
        {
            using (TextPrompt dialog = new TextPrompt("保存工作文件夹模板", "模板名称", project.TemplateName == "自定义（未保存）" ? "我的分类模板" : project.TemplateName))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string name = NameRules.SafeStem(dialog.Value, "分类模板");
                string path = Path.Combine(AppPaths.Templates, name + ".json");
                if (File.Exists(path) && MessageBox.Show(this, "该模板已存在，是否覆盖？", "模板", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                FolderTemplate template = new FolderTemplate { Name = name, Categories = JsonFile.Clone(project.Categories), UpdatedUtc = DateTime.UtcNow };
                JsonFile.Save(path, template);
                project.TemplateName = name;
                MarkProjectDirty();
                RefreshTemplateList();
            }
        }

        private void OpenProject(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "SW 实体分类项目 (*.swbody.json)|*.swbody.json|JSON 文件 (*.json)|*.json", Title = "打开项目" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { OpenProjectFile(dialog.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "项目无法打开", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void SaveProject(object sender, EventArgs e)
        {
            SaveProjectInteractive();
        }

        private void RemoveSelectedSource(object sender, EventArgs e)
        {
            SourceListItem selected = sourceList.SelectedItem as SourceListItem;
            if (selected == null || string.IsNullOrWhiteSpace(selected.Id) || worker.IsBusy) return;
            project.Sources.RemoveAll(item => item.Id == selected.Id);
            MarkProjectDirty();
            RefreshSources();
            RefreshGrid();
        }

        private void ChooseOutput(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择主输出文件夹。程序只会在此目录内创建分类文件夹与导出文件。", SelectedPath = Directory.Exists(outputBox.Text) ? outputBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
                if (dialog.ShowDialog(this) == DialogResult.OK) outputBox.Text = dialog.SelectedPath;
        }

        private void OpenOutputFolder(object sender, EventArgs e)
        {
            string path = outputBox.Text.Trim();
            if (!Directory.Exists(path)) { MessageBox.Show(this, "输出目录尚未创建。", "打开目录"); return; }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        private void UpdateEnvironmentSummary()
        {
            List<string> versions = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SolidWorks"))
                {
                    if (root != null)
                    {
                        foreach (string name in root.GetSubKeyNames())
                        {
                            const string prefix = "SOLIDWORKS ";
                            int year;
                            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !int.TryParse(name.Substring(prefix.Length), out year)) continue;
                            using (RegistryKey setup = root.OpenSubKey(name + @"\Setup"))
                            {
                                string installFolder = setup == null ? null : Convert.ToString(setup.GetValue("SolidWorks Folder"));
                                if (!string.IsNullOrWhiteSpace(installFolder) && File.Exists(Path.Combine(installFolder, "SLDWORKS.exe"))) versions.Add(name);
                            }
                        }
                    }
                }
            }
            catch { }
            string latest = versions.OrderByDescending(item => item, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            environmentLabel.Text = versions.Count == 0 ? UiText.T("● 未发现 SolidWorks", "● SolidWorks not found") : "● " + latest + UiText.T(" 已安装 · 点击检测", " installed · click to test");
            environmentLabel.ForeColor = versions.Count == 0 ? Color.Firebrick : Color.FromArgb(75, 81, 90);
        }

        private void DrawSourceItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= sourceList.Items.Count) return;
            SourceListItem item = sourceList.Items[e.Index] as SourceListItem;
            e.DrawBackground();
            Color primary = (e.State & DrawItemState.Selected) != 0 ? Color.White : Color.FromArgb(48, 53, 61);
            Color secondary = (e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(242, 246, 248) : Color.FromArgb(110, 116, 125);
            Rectangle nameBounds = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + 7, Math.Max(20, e.Bounds.Width - 18), 20);
            Rectangle summaryBounds = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + 28, Math.Max(20, e.Bounds.Width - 18), 17);
            using (Font nameFont = new Font(Font, item != null && string.IsNullOrWhiteSpace(item.Id) ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(e.Graphics, item == null ? string.Empty : item.Name, nameFont, nameBounds, primary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (Font summaryFont = new Font("Microsoft YaHei UI", 8F))
                TextRenderer.DrawText(e.Graphics, item == null ? string.Empty : item.Summary, summaryFont, summaryBounds, secondary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private void ShowSourceToolTip(object sender, MouseEventArgs e)
        {
            int index = sourceList.IndexFromPoint(e.Location);
            SourceListItem item = index >= 0 && index < sourceList.Items.Count ? sourceList.Items[index] as SourceListItem : null;
            toolTip.SetToolTip(sourceList, item == null || string.IsNullOrWhiteSpace(item.Path) ? "全部源文件" : item.Path);
        }

        private static string ShortSourceStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return UiText.T("待读取", "Pending");
            if (status.Contains("失败")) return UiText.T("读取失败", "Scan failed");
            if (status.Contains("完成")) return UiText.T("读取完成", "Scanned");
            if (status.Contains("正在")) return UiText.T("处理中", "Processing");
            return status;
        }

        private static string ShortBodyStatus(BodyRecord body)
        {
            if (body == null) return UiText.T("未处理", "Pending");
            if (!string.IsNullOrWhiteSpace(body.Status) && body.Status.Contains("失败")) return UiText.T("× 失败", "× Failed");
            if (body.CategoryId == CategoryNode.UnclassifiedId) return UiText.T("! 未分类", "! Unclassified");
            if (!string.IsNullOrWhiteSpace(body.Status) && body.Status.Contains("验证通过")) return UiText.T("✓ 已验证", "✓ Verified");
            if (!string.IsNullOrWhiteSpace(body.Status) && body.Status.Contains("正在")) return UiText.T("↻ 处理中", "↻ Processing");
            return UiText.T("✓ 就绪", "✓ Ready");
        }

        private void UpdateSelectionSummary()
        {
            int total = project.AllBodies().Count();
            string sourceFilter = (sourceList.SelectedItem as SourceListItem) == null ? string.Empty : ((SourceListItem)sourceList.SelectedItem).Id;
            int visibleRaw = string.IsNullOrWhiteSpace(sourceFilter) ? total : project.AllBodies().Count(item => item.SourceId == sourceFilter);
            int selected = project.Export.Deduplicate
                ? project.AllBodies().GroupBy(GeometryGroupKey).Count(group => group.First().ExportSelected)
                : project.AllBodies().Count(item => item.ExportSelected);
            int visibleSelected = bodyGrid.Rows.Cast<DataGridViewRow>().Count(row => (row.Tag as BodyRecord) != null && ((BodyRecord)row.Tag).ExportSelected);
            countLabel.Text = project.Export.Deduplicate
                ? string.Format(UiText.T("{0} 组 / {1} 个实体 · 已选 {2}", "{0} groups / {1} bodies · selected {2}"), bodyGrid.Rows.Count, visibleRaw, visibleSelected)
                : string.Format(UiText.T("{0} 个实体 · 已选 {1}", "{0} bodies · selected {1}"), bodyGrid.Rows.Count, visibleSelected);
            if (worker.IsBusy) return;
            bool hasFormat = sldprtCheck.Checked || stepCheck.Checked;
            exportButton.Enabled = selected > 0 && hasFormat;
            exportButton.Text = selected == 0
                ? UiText.T("请选择需要导出的实体", "Select bodies to export")
                : string.Format(UiText.T("导出 {0} 个实体{1}", "Export {0} part(s){1}"), selected, assemblyCheck.Checked ? UiText.T(" + 装配体", " + assembly") : string.Empty);
        }

        private static TabPage PreviewTab(string title, PictureBox box)
        {
            TabPage page = new TabPage(title) { BackColor = Color.FromArgb(248, 249, 250), Padding = new Padding(6) };
            page.Controls.Add(box);
            return page;
        }

        private static Label ToolbarCaption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(112, 118, 127), Padding = new Padding(4, 8, 2, 0), Margin = new Padding(1, 0, 1, 0) };
        }

        private static Panel ToolbarDivider()
        {
            return new Panel { Width = 1, Height = 23, BackColor = Color.FromArgb(222, 225, 229), Margin = new Padding(8, 5, 8, 0) };
        }

        private void ShowSafetyDetails(object sender, EventArgs e)
        {
            MessageBox.Show(this,
                "安全导出机制\n\n" +
                "• 源文件以只读方式打开\n• 导出前检查源文件是否变化\n• 结果先写入隔离暂存区\n" +
                "• SLDPRT 重新打开并验证为单实体\n• 验证通过后才复制到正式目录\n" +
                "• 覆盖旧文件前自动备份\n• STEP 失败不会回滚已验证的 SLDPRT\n" +
                "• 读取成功后保留 SolidWorks 与源零件以便定位；检测、失败或取消仍按安全规则清理\n" +
                "• 用户原有会话会恢复活动文档和界面状态，但成功读取的源零件保持打开\n" +
                "• 检测到活动文档被切换或会话被关闭时停止任务并提醒",
                "安全导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool ConfirmSolidWorksTask(string action, string taskDescription, bool keepSourcesOpen)
        {
            int runningCount = CountSolidWorksProcesses();

            if (runningCount > 1)
            {
                MessageBox.Show(this,
                    UiText.IsEnglish
                        ? "Multiple SolidWorks sessions are running. To avoid connecting to the wrong window, this " + action + " will not start.\n\nKeep only the SolidWorks session this program should use, then retry."
                        : "检测到多个正在运行的 SolidWorks。为避免连接到错误窗口，本次" + action + "不会开始。\n\n请只保留需要让导出程序使用的一个 SolidWorks，再重试。",
                    UiText.T("无法安全使用 SolidWorks", "SolidWorks cannot be used safely"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (runningCount == 0)
            {
                DialogResult startupChoice = ShowSolidWorksStartChoice(action, taskDescription, keepSourcesOpen);
                if (startupChoice == DialogResult.Cancel) return false;
                if (startupChoice == DialogResult.Yes)
                {
                    if (LaunchAuthorizedSolidWorks()) return true;
                    return WaitForManuallyStartedSolidWorks();
                }

                int manuallyStartedCount = CountSolidWorksProcesses();
                if (manuallyStartedCount != 1)
                {
                    MessageBox.Show(this,
                        manuallyStartedCount == 0
                            ? UiText.T("尚未检测到 SolidWorks。请先手动打开 SolidWorks，再重新开始本次任务。", "SolidWorks is not running yet. Start it manually, then begin this task again.")
                            : UiText.T("检测到多个 SolidWorks 进程。请只保留需要连接的一个会话，再重新开始本次任务。", "Multiple SolidWorks processes were found. Keep only the session to use, then begin this task again."),
                        UiText.T("无法连接手动会话", "Could not connect to manual session"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                authorizedSolidWorksProcessId = 0;
                authorizedSolidWorksStartTimeUtcTicks = 0;
                return true;
            }

            string sessionNotice = UiText.IsEnglish
                ? "A SolidWorks session you opened was detected. This program will use that window for the " + action + ".\n\nDo not operate, switch, or close SolidWorks until the task finishes. The program will not close your session and will restore the previously active document and window state." + (keepSourcesOpen ? " Successfully read source parts will remain open for body location." : string.Empty)
                : "检测到您已经手动打开了 SolidWorks。本程序将使用当前 SolidWorks 窗口及其相关功能完成本次" + action + "。\n\n在" + action + "完成之前，请不要操作、切换或关闭 SolidWorks。任务结束后，程序不会关闭您原来的 SolidWorks，并会恢复任务开始前的活动文档和界面状态。" + (keepSourcesOpen ? "成功读取的源零件会继续保持打开，以便使用实体定位。" : string.Empty);

            bool approved = MessageBox.Show(this,
                sessionNotice + "\n\n" + (UiText.IsEnglish
                    ? "Task: " + taskDescription + ".\nIf the SolidWorks session, active document, or a critical save step is disturbed, output stops and a warning appears.\n\nSave anything you are editing first. Start the " + action + "?"
                    : "本次工作内容：" + taskDescription + "。\n如果程序检测到 SolidWorks 会话、活动文档或关键保存过程受到干扰，将立即停止相关输出并弹窗提醒。\n\n请先保存正在编辑的内容。是否确认开始" + action + "？"),
                UiText.IsEnglish ? "Confirm " + action : action + "前确认",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
            if (!approved) return false;
            authorizedSolidWorksProcessId = 0;
            authorizedSolidWorksStartTimeUtcTicks = 0;
            return true;
        }

        private static int CountSolidWorksProcesses()
        {
            Process[] running = new Process[0];
            try
            {
                running = Process.GetProcessesByName("SLDWORKS");
                return running.Length;
            }
            catch { return 0; }
            finally { foreach (Process process in running) process.Dispose(); }
        }

        private bool WaitForManuallyStartedSolidWorks()
        {
            while (true)
            {
                DialogResult answer = MessageBox.Show(this,
                    UiText.T(
                        "程序已转入手动启动模式。\n\n请通过 Windows 开始菜单或桌面快捷方式启动 SolidWorks。看到 SolidWorks 主界面后，返回此处点击“重试”。\n\n程序只会连接唯一的一个 SolidWorks 会话，并会继续刚才的任务。主页面另设有“打开 SolidWorks”按钮，可在开始任务前使用。",
                        "The program has switched to manual startup mode.\n\nStart SolidWorks from the Windows Start menu or a desktop shortcut. When its main window appears, return here and click Retry.\n\nThe program will connect only when exactly one SolidWorks session is running, then continue the current task. The main page also has an 'Open SolidWorks' button for use before starting a task."),
                    UiText.T("请手动打开 SolidWorks", "Start SolidWorks manually"), MessageBoxButtons.RetryCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.Retry) return false;
                int count = CountSolidWorksProcesses();
                if (count == 1)
                {
                    authorizedSolidWorksProcessId = 0;
                    authorizedSolidWorksStartTimeUtcTicks = 0;
                    return true;
                }
                MessageBox.Show(this,
                    count == 0
                        ? UiText.T("仍未检测到 SolidWorks，请完成启动后再重试。", "SolidWorks is still not running. Finish starting it, then retry.")
                        : UiText.T("检测到多个 SolidWorks 进程。请只保留需要连接的一个会话，再重试。", "Multiple SolidWorks processes were found. Keep only the session to use, then retry."),
                    UiText.T("尚不能继续", "Cannot continue yet"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool IsAutomaticSolidWorksConnectionFailure(WorkerResponse response)
        {
            if (response == null || response.Success || string.IsNullOrWhiteSpace(response.Message)) return false;
            string message = response.Message;
            return message.IndexOf("90 秒内未能连接自动化接口", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("活动对象", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("进程身份校验失败", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("用户授权启动的 SolidWorks 进程", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OpenSolidWorksManually(object sender, EventArgs e)
        {
            if (worker.IsBusy)
            {
                MessageBox.Show(this, UiText.T("请等待当前读取或导出任务结束。", "Wait for the current scan or export task to finish."), UiText.T("任务进行中", "Task in progress"));
                return;
            }
            int runningCount = CountSolidWorksProcesses();
            if (runningCount == 1)
            {
                MessageBox.Show(this, UiText.T("SolidWorks 已经在运行，可以直接开始读取或导出。", "SolidWorks is already running. You can start scanning or exporting."), UiText.T("SolidWorks 已打开", "SolidWorks is open"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (runningCount > 1)
            {
                MessageBox.Show(this, UiText.T("检测到多个 SolidWorks 进程。请只保留需要使用的一个会话。", "Multiple SolidWorks processes were found. Keep only the session you want to use."), UiText.T("无法安全连接", "Cannot connect safely"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                UiText.T("是否现在打开 SolidWorks？\n\n这是由你主动点击的手动启动。程序不会把该会话视为自动化专用进程，也不会在任务结束时自行关闭它。",
                    "Open SolidWorks now?\n\nThis is a manual startup initiated by you. The program will not treat this session as an automation-owned process and will not close it when a task finishes."),
                UiText.T("打开 SolidWorks", "Open SolidWorks"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            string executable;
            if (!AssemblyStepExporter.TryFindSolidWorksExecutable(out executable))
            {
                MessageBox.Show(this, UiText.T("未找到 SLDWORKS.exe。请使用 Windows 开始菜单或桌面快捷方式手动打开 SolidWorks。", "SLDWORKS.exe was not found. Start SolidWorks from the Windows Start menu or a desktop shortcut."), UiText.T("无法打开 SolidWorks", "Could not open SolidWorks"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                using (Process process = Process.Start(info)) { }
                authorizedSolidWorksProcessId = 0;
                authorizedSolidWorksStartTimeUtcTicks = 0;
                progressLabel.Text = UiText.T("已请求手动打开 SolidWorks；请等待主界面完全显示后再开始任务。", "SolidWorks startup was requested; wait for its main window before starting a task.");
                MessageBox.Show(this, progressLabel.Text, UiText.T("正在打开 SolidWorks", "Opening SolidWorks"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, UiText.T("打开 SolidWorks 失败：\n", "Could not open SolidWorks:\n") + ex.Message + "\n\n" + UiText.T("请改用 Windows 开始菜单或桌面快捷方式手动打开。", "Start it manually from the Windows Start menu or a desktop shortcut."), UiText.T("无法打开 SolidWorks", "Could not open SolidWorks"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DialogResult ShowSolidWorksStartChoice(string action, string taskDescription, bool keepSourcesOpen)
        {
            using (Form dialog = new Form())
            {
                UiBrand.ApplyIcon(dialog);
                dialog.Text = UiText.T("选择 SolidWorks 启动方式", "Choose how to start SolidWorks");
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(680, 330);
                dialog.Font = Font;

                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(24, 20, 24, 18), BackColor = Color.White };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                layout.Controls.Add(new Label
                {
                    Text = UiText.T("没有检测到正在运行的 SolidWorks", "SolidWorks is not running"),
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                    ForeColor = BrandRed,
                    Margin = new Padding(0, 0, 0, 14)
                }, 0, 0);
                string lifecycle = keepSourcesOpen
                    ? UiText.T("读取完成后，SolidWorks 和成功读取的源零件将保持打开，便于直接定位实体。", "After scanning, SolidWorks and the successfully read source parts will remain open for body location.")
                    : UiText.T("如果由程序启动，任务结束后程序会关闭本次启动的 SolidWorks。", "If the program starts SolidWorks, it will close that session when this task finishes.");
                layout.Controls.Add(new Label
                {
                    Text = UiText.T(
                        "本次工作：" + taskDescription + "。\n\n你可以授权程序自动打开 SolidWorks；也可以先通过 Windows 开始菜单或桌面快捷方式自行打开 SolidWorks，再返回这里选择“我已手动打开”。手动打开的会话不会被程序关闭。主页面另设有“打开 SolidWorks”按钮，可在开始任务前使用。\n\n任务执行期间请不要操作、切换或关闭 SolidWorks。\n\n" + lifecycle,
                        "Task: " + taskDescription + ".\n\nYou can authorize the program to start SolidWorks, or start SolidWorks yourself from Windows and then return here and choose 'I opened it manually'. A manually started session will not be closed by this program. The main page also has an 'Open SolidWorks' button for use before starting a task.\n\nDo not operate, switch, or close SolidWorks while the task is running.\n\n" + lifecycle),
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    ForeColor = Color.FromArgb(55, 61, 70)
                }, 0, 1);

                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
                Button cancel = new Button { Text = UiText.T("取消", "Cancel"), DialogResult = DialogResult.Cancel, Width = 92, Height = 32 };
                Button manual = new Button { Text = UiText.T("我已手动打开", "I opened it manually"), DialogResult = DialogResult.No, Width = 156, Height = 32 };
                Button automatic = new Button { Text = UiText.T("由程序自动打开", "Open automatically"), DialogResult = DialogResult.Yes, Width = 156, Height = 32, BackColor = BrandRed, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(manual);
                buttons.Controls.Add(automatic);
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = automatic;
                dialog.CancelButton = cancel;
                return dialog.ShowDialog(this);
            }
        }

        private bool LaunchAuthorizedSolidWorks()
        {
            string executable;
            if (!AssemblyStepExporter.TryFindSolidWorksExecutable(out executable))
            {
                MessageBox.Show(this, UiText.T("没有找到可自动启动的 SLDWORKS.exe。程序将转入手动启动模式。", "SLDWORKS.exe could not be found for automatic startup. The program will switch to manual startup mode."), UiText.T("自动启动失败", "Automatic startup failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                using (Process process = Process.Start(info))
                {
                    if (process == null) throw new InvalidOperationException("Windows 没有返回 SolidWorks 进程。");
                    authorizedSolidWorksProcessId = process.Id;
                    authorizedSolidWorksStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                }
                return true;
            }
            catch (Exception ex)
            {
                authorizedSolidWorksProcessId = 0;
                authorizedSolidWorksStartTimeUtcTicks = 0;
                MessageBox.Show(this, UiText.T("自动打开 SolidWorks 失败：\n", "SolidWorks could not be started automatically:\n") + ex.Message + "\n\n" + UiText.T("程序将转入手动启动模式。", "The program will switch to manual startup mode."), UiText.T("自动启动失败", "Automatic startup failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void OpenLastReport(object sender, EventArgs e)
        {
            if (!File.Exists(lastReportPath)) { MessageBox.Show(this, "尚无可打开的导出报表。", "查看报表"); return; }
            Process.Start(new ProcessStartInfo { FileName = lastReportPath, UseShellExecute = true });
        }

        private void RefreshCategoryTree()
        {
            if (categoryTreeRefreshing || categoryTree.IsDisposed) return;
            categoryTreeRefreshing = true;
            string selectedId = categoryTree.SelectedNode == null || categoryTree.SelectedNode.Tag == null ? CategoryNode.RootId : ((CategoryNode)categoryTree.SelectedNode.Tag).Id;
            categoryTree.BeginUpdate();
            categoryTree.Nodes.Clear();
            CategoryNode root = project.Categories.FirstOrDefault(item => item.Id == CategoryNode.RootId);
            if (root != null)
            {
                TreeNode rootNode = BuildCategoryTreeNode(root);
                categoryTree.Nodes.Add(rootNode);
                AddCategoryChildren(rootNode, root.Id);
                rootNode.Expand();
            }
            categoryTree.EndUpdate();
            categoryTreeRefreshing = false;
            SelectCategoryTreeNode(selectedId);
        }

        private TreeNode BuildCategoryTreeNode(CategoryNode node)
        {
            int count = project.AllBodies().Count(item => CategoryRules.IsDescendant(project.Categories, item.CategoryId, node.Id));
            if (node.Id == CategoryNode.RootId) count = project.AllBodies().Count();
            return new TreeNode(string.Format("{0}  ({1})", node.Name, count)) { Tag = node, ToolTipText = CategoryRules.GetPath(project.Categories, node.Id) };
        }

        private void AddCategoryChildren(TreeNode parent, string parentId)
        {
            foreach (CategoryNode child in project.Categories.Where(item => item.ParentId == parentId).OrderBy(item => item.Order).ThenBy(item => item.Name))
            {
                TreeNode childNode = BuildCategoryTreeNode(child);
                parent.Nodes.Add(childNode);
                AddCategoryChildren(childNode, child.Id);
            }
        }

        private CategoryNode SelectedCategoryNode()
        {
            CategoryNode treeNode = categoryTree.SelectedNode == null ? null : categoryTree.SelectedNode.Tag as CategoryNode;
            return treeNode ?? folderCanvas.SelectedNode;
        }

        private void SelectCategoryTreeNode(string id)
        {
            TreeNode node = FindCategoryTreeNode(categoryTree.Nodes, id);
            if (node != null) categoryTree.SelectedNode = node;
        }

        private static TreeNode FindCategoryTreeNode(TreeNodeCollection nodes, string id)
        {
            foreach (TreeNode node in nodes)
            {
                CategoryNode category = node.Tag as CategoryNode;
                if (category != null && category.Id == id) return node;
                TreeNode nested = FindCategoryTreeNode(node.Nodes, id);
                if (nested != null) return nested;
            }
            return null;
        }

        private void CategoryTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (categoryTreeRefreshing) return;
            CategoryNode node = e.Node == null ? null : e.Node.Tag as CategoryNode;
            if (node != null) folderCanvas.SelectedId = node.Id;
        }

        private void CategoryTreeItemDrag(object sender, ItemDragEventArgs e)
        {
            TreeNode node = e.Item as TreeNode;
            CategoryNode category = node == null ? null : node.Tag as CategoryNode;
            if (category != null && !category.IsSystem) categoryTree.DoDragDrop(node, DragDropEffects.Move);
        }

        private void CategoryTreeDragEnter(object sender, DragEventArgs e)
        {
            Point point = categoryTree.PointToClient(new Point(e.X, e.Y));
            TreeNode target = categoryTree.GetNodeAt(point);
            CategoryNode category = target == null ? null : target.Tag as CategoryNode;
            e.Effect = category != null && category.Id != CategoryNode.UnclassifiedId && (e.Data.GetDataPresent(typeof(TreeNode)) || e.Data.GetDataPresent(typeof(BodyRecord))) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void CategoryTreeDragDrop(object sender, DragEventArgs e)
        {
            Point point = categoryTree.PointToClient(new Point(e.X, e.Y));
            TreeNode targetTreeNode = categoryTree.GetNodeAt(point);
            CategoryNode target = targetTreeNode == null ? null : targetTreeNode.Tag as CategoryNode;
            if (target == null || target.Id == CategoryNode.UnclassifiedId) return;
            BodyRecord body = e.Data.GetData(typeof(BodyRecord)) as BodyRecord;
            if (body != null)
            {
                foreach (BodyRecord member in GetGroupMembers(body)) member.CategoryId = target.Id == CategoryNode.RootId ? CategoryNode.UnclassifiedId : target.Id;
                MarkProjectDirty();
                RefreshGrid();
                return;
            }
            TreeNode sourceTreeNode = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            CategoryNode source = sourceTreeNode == null ? null : sourceTreeNode.Tag as CategoryNode;
            if (source == null || source.IsSystem || source.Id == target.Id || CategoryRules.IsDescendant(project.Categories, target.Id, source.Id)) return;
            source.ParentId = target.Id;
            source.Order = project.Categories.Count;
            folderCanvas.Nodes = project.Categories;
            folderCanvas.SelectedId = source.Id;
            CategoryTreeChanged();
            SelectCategoryTreeNode(source.Id);
        }

        private void BodyGridMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || (Math.Abs(e.X - gridDragStart.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - gridDragStart.Y) < SystemInformation.DragSize.Height / 2)) return;
            DataGridView.HitTestInfo hit = bodyGrid.HitTest(gridDragStart.X, gridDragStart.Y);
            if (hit.RowIndex < 0) return;
            BodyRecord body = bodyGrid.Rows[hit.RowIndex].Tag as BodyRecord;
            if (body != null) bodyGrid.DoDragDrop(body, DragDropEffects.Move);
        }

        private void MainDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void MainDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddSourcePaths(paths);
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

        private static string FromBase64(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); } catch { return value; }
        }

        private static Image LoadImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try { using (Image source = Image.FromFile(path)) return new Bitmap(source); } catch { return null; }
        }

        private static void SetPicture(PictureBox box, string path)
        {
            Image old = box.Image;
            box.Image = LoadImage(path);
            if (old != null) old.Dispose();
        }

        private static PictureBox CreatePictureBox()
        {
            return new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 249, 250), SizeMode = PictureBoxSizeMode.Zoom };
        }

        private static Control WrapPreview(string title, PictureBox box)
        {
            GroupBox group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6) };
            group.Controls.Add(box);
            return group;
        }

        private static Panel Card()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8) };
        }

        private static Label SectionTitle(string text)
        {
            return new Label { Text = text, AutoSize = true, Height = 34, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(41, 46, 54), Padding = new Padding(3, 5, 0, 0) };
        }

        private Button MakeButton(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, Height = 30, BackColor = Color.White, ForeColor = Color.FromArgb(55, 61, 70), FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 0, 3, 0) };
            button.FlatAppearance.BorderColor = Color.FromArgb(211, 215, 221);
            button.Click += click;
            return button;
        }

        private Button MakeSmallButton(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, Height = 28, BackColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 0, 2, 0) };
            button.FlatAppearance.BorderColor = Color.FromArgb(211, 215, 221);
            button.Click += click;
            return button;
        }

        private sealed class SourceListItem
        {
            public string Id;
            public string Name;
            public string Summary;
            public string Status;
            public string Path;
            public override string ToString() { return Name; }
        }

        private sealed class TemplateItem
        {
            public string Name;
            public string Path;
            public override string ToString() { return Name; }
        }

        private sealed class WorkerJob { public string RequestPath; public string ResponsePath; }
        private sealed class WorkerProgress { public string Stage; public string Detail; }
    }

    internal sealed class TextPrompt : Form
    {
        private readonly TextBox input = new TextBox();
        public string Value { get { return input.Text.Trim(); } }

        public TextPrompt(string title, string label, string value)
        {
            UiBrand.ApplyIcon(this);
            Text = title; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(400, 130); Font = new Font("Microsoft YaHei UI", 9F);
            Label caption = new Label { Text = label, AutoSize = true, Location = new Point(18, 18) };
            input.Text = value; input.Location = new Point(20, 43); input.Width = 360;
            Button ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(224, 84), Width = 74 };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(306, 84), Width = 74 };
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(caption); Controls.Add(input); Controls.Add(ok); Controls.Add(cancel);
            Shown += delegate { input.Focus(); input.SelectAll(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(Value)) { MessageBox.Show(this, "名称不能为空。", "名称"); e.Cancel = true; } };
        }
    }

    internal sealed class CategoryDialog : Form
    {
        private readonly TextBox input = new TextBox();
        private readonly ComboBox parents = new ComboBox();
        public string CategoryName { get { return NameRules.SafeStem(input.Text, "分类"); } }
        public string ParentId { get { return ((ParentItem)parents.SelectedItem).Id; } }

        public CategoryDialog(List<CategoryNode> categories, string parentId, string title, string value)
        {
            UiBrand.ApplyIcon(this);
            Text = title; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(430, 205); Font = new Font("Microsoft YaHei UI", 9F);
            Controls.Add(new Label { Text = "名称（该名称同时作为零件标签与输出文件夹）", AutoSize = true, Location = new Point(18, 18) });
            input.Text = value; input.Location = new Point(20, 45); input.Width = 388; Controls.Add(input);
            Controls.Add(new Label { Text = "放在以下父文件夹内", AutoSize = true, Location = new Point(18, 82) });
            parents.Location = new Point(20, 108); parents.Width = 388; parents.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (CategoryNode node in categories.Where(item => item.Id != CategoryNode.UnclassifiedId))
                parents.Items.Add(new ParentItem { Id = node.Id, Caption = node.Id == CategoryNode.RootId ? node.Name : node.Name + "  ·  " + CategoryRules.GetPath(categories, node.Id) });
            for (int i = 0; i < parents.Items.Count; i++) if (((ParentItem)parents.Items[i]).Id == parentId) { parents.SelectedIndex = i; break; }
            if (parents.SelectedIndex < 0) parents.SelectedIndex = 0;
            Controls.Add(parents);
            Button ok = new Button { Text = "创建", DialogResult = DialogResult.OK, Location = new Point(252, 158), Width = 74 };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(334, 158), Width = 74 };
            AcceptButton = ok; CancelButton = cancel; Controls.Add(ok); Controls.Add(cancel);
            Shown += delegate { input.Focus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(input.Text)) { MessageBox.Show(this, "名称不能为空。", "名称"); e.Cancel = true; } };
        }

        private sealed class ParentItem { public string Id; public string Caption; public override string ToString() { return Caption; } }
    }
}
