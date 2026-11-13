using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Blankedge.SWHoleTools.BatchExporter.Configuration;
using Blankedge.SWHoleTools.BatchExporter.Model;
using Blankedge.SWHoleTools.BatchExporter.SolidWorks;

namespace Blankedge.SWHoleTools.BatchExporter
{
    public sealed class MainForm : Form
    {
        private readonly DataGridView _files = new DataGridView();
        private readonly BindingList<DrawingExportJob> _jobs = new BindingList<DrawingExportJob>();
        private readonly TextBox _prefix = new TextBox();
        private readonly TextBox _batchScale = new TextBox { Text = "1:1", Width = 65 };
        private readonly ComboBox _scaleScope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
        private readonly TextBox _outputDirectory = new TextBox();
        private readonly TextBox _templatePath = new TextBox();
        private readonly ComboBox _paper = new ComboBox();
        private readonly ComboBox _preset = new ComboBox();
        private readonly TextBox _viewNames = new TextBox();
        private readonly CheckBox _slddrw = new CheckBox { Text = "SLDDRW", Checked = true, AutoSize = true };
        private readonly CheckBox _pdf = new CheckBox { Text = "PDF", AutoSize = true };
        private readonly CheckBox _dwg = new CheckBox { Text = "DWG", AutoSize = true };
        private readonly CheckBox _dxf = new CheckBox { Text = "DXF", AutoSize = true };
        private Control _editor;
        private readonly CheckBox _annotations = new CheckBox { Text = "读取孔标签并添加工程图标注", Checked = true, AutoSize = true };
        private readonly CheckBox _dryRun = new CheckBox { Text = "只读 Dry Run 分析，不生成或保存工程图", Checked = false, AutoSize = true };
        private readonly CheckBox _overwrite = new CheckBox { Text = "允许覆盖同名输出文件", Checked = false, AutoSize = true };
        private readonly ComboBox _connection = new ComboBox();
        private readonly TextBox _log = new TextBox();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Button _start = new Button();
        private readonly Button _cancel = new Button();
        private readonly ViewPresetStore _presetStore;
        private CancellationTokenSource _cancellation;
        private bool _running;
        private bool _loadingPreset;
        public bool IsRunning => _running;

        public MainForm()
        {
            _presetStore = ViewPresetStore.Load();
            Text = "Blankedge SOLIDWORKS 批量工程图导出";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 700);
            Size = new Size(1180, 760);
            Font = new Font("Microsoft YaHei UI", 9F);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += OnFormClosing;
            BuildUi();
            LoadPresets();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(root);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 500, FixedPanel = FixedPanel.Panel2 };
            split.SizeChanged += (sender, e) =>
            {
                if (split.ClientSize.Width >= 850) split.SplitterDistance = split.ClientSize.Width - 550;
            };
            split.Panel1.Controls.Add(BuildFilePanel());
            split.Panel2.Controls.Add(BuildSettingsPanel());
            root.Controls.Add(split, 0, 0);
            _editor = split;

            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Both;
            _log.Font = new Font("Consolas", 9F);
            root.Controls.Add(_log, 0, 1);

            var commands = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Padding = new Padding(0, 8, 0, 0) };
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 1; i < 5; i++) commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            _progress.Dock = DockStyle.Fill;
            _progress.Style = ProgressBarStyle.Continuous;
            commands.Controls.Add(_progress, 0, 0);
            var preflight = new Button { Text = "静态预检", Dock = DockStyle.Fill };
            preflight.Click += (sender, args) => Preflight();
            _start.Text = "开始导出";
            _start.Dock = DockStyle.Fill;
            _start.Click += (sender, args) => StartExport();
            _cancel.Text = "取消";
            _cancel.Dock = DockStyle.Fill;
            _cancel.Enabled = false;
            _cancel.Click += (sender, args) => _cancellation?.Cancel();
            var openOutput = new Button { Text = "打开输出目录", Dock = DockStyle.Fill };
            openOutput.Click += (sender, args) => OpenOutputDirectory();
            commands.Controls.Add(preflight, 1, 0);
            commands.Controls.Add(_start, 2, 0);
            commands.Controls.Add(_cancel, 3, 0);
            commands.Controls.Add(openOutput, 4, 0);
            root.Controls.Add(commands, 0, 2);
        }

        private Control BuildFilePanel()
        {
            var group = new GroupBox { Text = "零件队列", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _files.Dock = DockStyle.Fill;
            _files.AutoGenerateColumns = false;
            _files.AllowUserToAddRows = false;
            _files.AllowUserToDeleteRows = false;
            _files.RowHeadersVisible = false;
            _files.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _files.MultiSelect = true;
            _files.BackgroundColor = Color.White;
            _files.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _files.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Included", HeaderText = "导出", Width = 44, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            _files.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OriginalName", HeaderText = "原名称", ReadOnly = true, MinimumWidth = 100 });
            _files.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ExportName", HeaderText = "导出名称", ReadOnly = true, MinimumWidth = 110 });
            _files.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ScaleText", HeaderText = "比例", Width = 80, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            _files.DataSource = _jobs;
            _files.AllowDrop = true;
            _files.DragEnter += OnDragEnter;
            _files.DragDrop += OnDragDrop;
            _files.CurrentCellDirtyStateChanged += (sender, e) => { if (_files.CurrentCell is DataGridViewCheckBoxCell) _files.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _files.CellValidating += (sender, e) =>
            {
                if (_files.Columns[e.ColumnIndex].DataPropertyName != "ScaleText") return;
                try { DrawingExportJob.ApplyScale(new BatchExportOptions(), Convert.ToString(e.FormattedValue)); _files.Rows[e.RowIndex].ErrorText = string.Empty; }
                catch (Exception ex) { e.Cancel = true; _files.Rows[e.RowIndex].ErrorText = ex.Message; AppendLog(ex.Message); }
            };
            _files.CellToolTipTextNeeded += (sender, e) => { if (e.RowIndex >= 0) e.ToolTipText = _jobs[e.RowIndex].SourcePath; };
            layout.Controls.Add(_files, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            var folder = new Button { Text = "添加文件夹", Width = 95, Height = 30 };
            folder.Click += (sender, e) => { using (var dialog = new FolderBrowserDialog { Description = "选择零件目录", ShowNewFolderButton = false }) if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(new[] { dialog.SelectedPath }); };
            var add = new Button { Text = "添加零件", Width = 80, Height = 30 };
            add.Click += (sender, args) => BrowseParts();
            var remove = new Button { Text = "移除选中", Width = 80, Height = 30 };
            remove.Click += (sender, args) => RemoveSelected();
            var all = new Button { Text = "全选导出", Width = 80, Height = 30 };
            all.Click += (sender, e) => { foreach (var job in _jobs) job.Included = true; _jobs.ResetBindings(); };
            buttons.Controls.AddRange(new Control[] { folder, add, remove, all });
            layout.Controls.Add(buttons, 0, 0);
            var scales = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            _scaleScope.Items.AddRange(new[] { "选中零件", "全部零件" });
            _scaleScope.SelectedIndex = 0;
            var apply = new Button { Text = "设置比例", Width = 80, Height = 28 };
            apply.Click += (sender, e) => ApplyBatchScale();
            var invert = new Button { Text = "反选导出", Width = 80, Height = 28 };
            invert.Click += (sender, e) => { foreach (var job in _jobs) job.Included = !job.Included; _jobs.ResetBindings(); };
            scales.Controls.AddRange(new Control[] { _batchScale, _scaleScope, apply, invert });
            layout.Controls.Add(scales, 0, 1);
            group.Controls.Add(layout);
            return group;
        }

        private Control BuildSettingsPanel()
        {
            var group = new GroupBox { Text = "工程图设置", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 486, ColumnCount = 3, RowCount = 12 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            for (int i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 10 ? 54 : 38));

            var outputBrowse = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            outputBrowse.Click += (sender, args) => BrowseOutputDirectory();
            AddRow(layout, 0, "输出目录", _outputDirectory, outputBrowse);
            var templateBrowse = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            templateBrowse.Click += (sender, args) => BrowseTemplate();
            AddRow(layout, 1, "DRWDOT 模板", _templatePath, templateBrowse);

            _paper.DropDownStyle = ComboBoxStyle.DropDownList;
            _paper.Items.AddRange(Enum.GetNames(typeof(PaperChoice)));
            _paper.SelectedItem = PaperChoice.Auto.ToString();
            AddRow(layout, 2, "纸张", _paper, null);

            AddRow(layout, 3, "统一前缀", _prefix, null);
            _prefix.TextChanged += (sender, e) => RefreshNames();

            _preset.DropDownStyle = ComboBoxStyle.DropDown;
            _preset.SelectedIndexChanged += (sender, args) => SelectPreset();
            var savePreset = new Button { Text = "保存方案", Dock = DockStyle.Fill };
            savePreset.Click += (sender, args) => SavePreset();
            AddRow(layout, 4, "视图方案", _preset, savePreset);
            _viewNames.PlaceholderTextCompat("*Front, *Top, *Right, *Isometric");
            AddRow(layout, 5, "模型视图名", _viewNames, null);

            var formats = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            formats.Controls.AddRange(new Control[] { _slddrw, _dwg, _pdf, _dxf });
            AddRow(layout, 6, "输出格式", formats, null);
            AddRow(layout, 7, "孔标注", _annotations, null);
            AddRow(layout, 8, "Dry Run", _dryRun, null);
            AddRow(layout, 9, "覆盖策略", _overwrite, null);

            _connection.DropDownStyle = ComboBoxStyle.DropDownList;
            _connection.Items.AddRange(new object[] { "仅连接当前 SOLIDWORKS（默认）", "无现有会话时启动独立隐藏会话" });
            _connection.SelectedIndex = 0;
            AddRow(layout, 10, "连接方式", _connection, null);
            var hint = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                AutoSize = false
            };
            layout.Controls.Add(hint, 1, 11);
            layout.SetColumnSpan(hint, 2);
            scroll.Controls.Add(layout);
            group.Controls.Add(scroll);
            return group;
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control main, Control action)
        {
            layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            main.Dock = DockStyle.Fill;
            layout.Controls.Add(main, 1, row);
            if (action != null) layout.Controls.Add(action, 2, row);
            else layout.SetColumnSpan(main, 2);
        }

        private void LoadPresets(ViewPreset selected = null)
        {
            _loadingPreset = true;
            _preset.Items.Clear();
            foreach (ViewPreset preset in _presetStore.Presets) _preset.Items.Add(preset);
            _preset.SelectedItem = selected ?? _preset.Items.Cast<object>().FirstOrDefault();
            _loadingPreset = false;
            SelectPreset();
        }

        private void SelectPreset()
        {
            if (_loadingPreset || !(_preset.SelectedItem is ViewPreset preset)) return;
            _viewNames.Text = string.Join(", ", preset.ModelViewNames);
        }

        private void SavePreset()
        {
            string name = _preset.Text.Trim();
            List<string> views = ParseViewNames();
            if (string.IsNullOrWhiteSpace(name) || views.Count == 0)
            {
                MessageBox.Show(this, "请在视图方案框输入名称，并至少填写一个模型视图名。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                var preset = new ViewPreset { Name = name, ModelViewNames = views };
                _presetStore.UpsertAndSave(preset);
                LoadPresets(_presetStore.Presets.First(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
                AppendLog("已保存视图方案：" + name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存视图方案失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private BatchExportOptions ReadOptions()
        {
            Enum.TryParse(_paper.Text, out PaperChoice paper);
            return new BatchExportOptions
            {
                OutputDirectory = _outputDirectory.Text.Trim(),
                DrawingTemplatePath = _templatePath.Text.Trim(),
                Paper = paper,
                NamePrefix = _prefix.Text,
                Views = new ViewPreset { Name = _preset.Text.Trim(), ModelViewNames = ParseViewNames() },
                SaveDrawing = _slddrw.Checked,
                SavePdf = _pdf.Checked,
                SaveDwg = _dwg.Checked,
                SaveDxf = _dxf.Checked,
                AddHoleAnnotations = _annotations.Checked,
                DryRunOnly = _dryRun.Checked,
                OverwriteExisting = _overwrite.Checked,
                ConnectionMode = _connection.SelectedIndex == 1 ? SolidWorksConnectionMode.StartHidden : SolidWorksConnectionMode.ExistingOnly
            };
        }

        private List<string> ParseViewNames()
        {
            return (_viewNames.Text ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IList<DrawingExportJob> ReadFiles()
        {
            if (!_files.EndEdit()) return null;
            return _jobs.Where(job => job.Included).Select(job => job.Snapshot()).ToList();
        }

        private bool ValidateInput(BatchExportOptions options, IList<DrawingExportJob> files)
        {
            if (files == null)
            {
                AppendLog("请先修正当前单元格中的比例。");
                return false;
            }
            var errors = DrawingExportJob.ValidateBatch(files, options);
            if (errors.Count == 0) return true;
            AppendLog("预检失败：\r\n- " + string.Join("\r\n- ", errors));
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void Preflight()
        {
            BatchExportOptions options = ReadOptions();
            IList<DrawingExportJob> files = ReadFiles();
            if (!ValidateInput(options, files)) return;
            int collisions = files.Count(file => options.OutputPaths(file.SourcePath).Any(File.Exists));
            AppendLog(string.Format(CultureInfo.CurrentCulture,
                "静态预检通过：{0} 个零件；模式 {1}；同名输出 {2} 个。此步骤未启动或连接 SOLIDWORKS。",
                files.Count, options.DryRunOnly ? "Dry Run" : options.OutputDirectory, collisions));
        }

        private void StartExport()
        {
            if (_running) return;
            BatchExportOptions options = ReadOptions();
            IList<DrawingExportJob> files = ReadFiles();
            if (!ValidateInput(options, files)) return;
            if (!options.DryRunOnly && options.OverwriteExisting && MessageBox.Show(this,
                    "已启用覆盖：所选格式的同名输出可能被替换。是否继续？", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            if (!options.DryRunOnly)
            {
                Directory.CreateDirectory(Path.GetFullPath(options.OutputDirectory));
                options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
            }
            SetRunning(true);
            _progress.Minimum = 0;
            _progress.Maximum = files.Count;
            _progress.Value = 0;
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            AppendLog(options.DryRunOnly ? "开始 Dry Run。源文件将以只读方式打开，不生成工程图。\r\n" : "开始批量导出。源文件将以只读方式打开。\r\n");

            var thread = new Thread(() =>
            {
                try
                {
                    new BatchExportRunner().Run(files, options, token, progress => SafeUi(() => ReportProgress(progress)));
                    SafeUi(() => FinishRun(token.IsCancellationRequested ? "任务已取消。" : "批量任务已结束。"));
                }
                catch (Exception ex)
                {
                    SafeUi(() => FinishRun("批量任务失败：" + ex.Message));
                }
            }) { IsBackground = true, Name = "Blankedge SOLIDWORKS Export STA" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void ReportProgress(BatchProgress progress)
        {
            _progress.Value = Math.Min(_progress.Maximum, progress.Completed);
            ExportResult result = progress.Result;
            string state = result.Success ? "成功" : result.Skipped ? "跳过" : "失败";
            string outputs = result.OutputPaths.Count == 0 ? string.Empty : " => " + string.Join(", ", result.OutputPaths);
            string dryRun = result.DryRunReport == null ? string.Empty : "；" + result.DryRunReport.Summary();
            AppendLog(string.Format("[{0}/{1}] {2} {3}: {4}{5}{6}", progress.Completed, progress.Total, state,
                Path.GetFileName(result.SourcePath), result.Message, dryRun, outputs));
        }

        private void FinishRun(string message)
        {
            AppendLog(message);
            SetRunning(false);
            _cancellation?.Dispose();
            _cancellation = null;
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _start.Enabled = !running;
            _cancel.Enabled = running;
            _editor.Enabled = !running;
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            if (_running) return;
            var existing = new HashSet<string>(_jobs.Select(job => job.SourcePath), StringComparer.OrdinalIgnoreCase);
            foreach (string path in PartFileDiscovery.Collect(paths, AppendLog))
            {
                if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase)) continue;
                string full = Path.GetFullPath(path);
                if (existing.Add(full)) _jobs.Add(new DrawingExportJob { SourcePath = full, ExportName = _prefix.Text + Path.GetFileNameWithoutExtension(full) });
            }
        }

        private void BrowseParts()
        {
            using (var dialog = new OpenFileDialog { Filter = "SOLIDWORKS 零件 (*.sldprt)|*.sldprt", Multiselect = true, CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames);
            }
        }

        private void RemoveSelected()
        {
            foreach (var selected in SelectedJobs()) _jobs.Remove(selected);
        }

        private DrawingExportJob[] SelectedJobs() => _files.SelectedRows.Cast<DataGridViewRow>().Select(row => row.DataBoundItem).OfType<DrawingExportJob>().ToArray();

        private void RefreshNames()
        {
            foreach (var job in _jobs) job.ExportName = _prefix.Text + job.OriginalName;
            _jobs.ResetBindings();
        }

        private void ApplyBatchScale()
        {
            try
            {
                DrawingExportJob.ApplyScale(new BatchExportOptions(), _batchScale.Text);
                var targets = _scaleScope.SelectedIndex == 1 ? _jobs.ToArray() : SelectedJobs();
                if (targets.Length == 0) { AppendLog("请先选中需要设置比例的零件。"); return; }
                foreach (var job in targets) job.ScaleText = _batchScale.Text.Trim();
                _jobs.ResetBindings();
            }
            catch (Exception ex) { AppendLog(ex.Message); }
        }

        private void BrowseOutputDirectory()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择工程图输出目录", ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _outputDirectory.Text = dialog.SelectedPath;
            }
        }

        private void BrowseTemplate()
        {
            using (var dialog = new OpenFileDialog { Filter = "SOLIDWORKS 工程图模板 (*.drwdot)|*.drwdot|所有文件 (*.*)|*.*", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _templatePath.Text = dialog.FileName;
            }
        }

        private void OpenOutputDirectory()
        {
            string path = _outputDirectory.Text.Trim();
            if (!Directory.Exists(path))
            {
                MessageBox.Show(this, "输出目录尚不存在。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !_running && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            AddFiles(e.Data.GetData(DataFormats.FileDrop) as string[]);
        }

        private void AppendLog(string message)
        {
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
        }

        private void SafeUi(Action action)
        {
            if (IsDisposed || Disposing) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_running) return;
            e.Cancel = true;
            _cancellation?.Cancel();
            MessageBox.Show(this, "正在安全取消；待当前 SOLIDWORKS API 调用返回后即可关闭。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal static class TextBoxCompatibilityExtensions
    {
        public static void PlaceholderTextCompat(this TextBox textBox, string placeholder)
        {
            textBox.Tag = placeholder;
            textBox.Text = placeholder;
            textBox.ForeColor = Color.Gray;
            textBox.GotFocus += (sender, args) =>
            {
                if (!string.Equals(textBox.Text, placeholder, StringComparison.Ordinal)) return;
                textBox.Text = string.Empty;
                textBox.ForeColor = SystemColors.WindowText;
            };
            textBox.LostFocus += (sender, args) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text)) return;
                textBox.Text = placeholder;
                textBox.ForeColor = Color.Gray;
            };
        }
    }
}
