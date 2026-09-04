using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SWBodyOrganizer
{
    public sealed partial class MainForm
    {
        private readonly Button guidedButton = new Button();
        private readonly Button locateButton = new Button();
        private readonly Button batchCategoryButton = new Button();
        private readonly ComboBox zoomCombo = new ComboBox();
        private readonly Timer autosaveTimer = new Timer();
        private string currentProjectPath = string.Empty;
        private bool projectDirty;
        private bool suppressDirty;
        private bool allowCloseWithoutPrompt;

        private void InitializeV120()
        {
            autosaveTimer.Interval = 2500;
            autosaveTimer.Tick += delegate { if (projectDirty && !worker.IsBusy) SaveProjectSilently(); };
            autosaveTimer.Start();
            FormClosed += delegate { autosaveTimer.Stop(); DisposeGridImages(); };
            ApplyListZoom(project.ListZoomPercent < 80 ? UserSettingsStore.Current.ListZoomPercent : project.ListZoomPercent);
            ApplyLanguage();
            UpdateWindowTitle();
            // The constructor runs before WinForms creates this form's native handle.
            // Schedule recovery inspection from Shown so a normal double-click launch
            // never calls BeginInvoke against a handle that does not exist yet.
            if (!Program.SuppressStartupPrompts) Shown += delegate { CheckRecoveryProject(); };
        }

        internal void CaptureGuidedScreenshot(string path)
        {
            List<BodyRecord> bodies = GetDisplayBodies(string.Empty);
            if (bodies.Count == 0) return;
            using (GuidedBodyForm guided = new GuidedBodyForm(project, bodies, GetGroupMembers, delegate { }, LocateBodiesInSolidWorks))
            {
                guided.StartPosition = FormStartPosition.Manual;
                guided.Location = new Point(-32000, -32000);
                guided.Size = new Size(1240, 820);
                guided.Show();
                Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(guided.Width, guided.Height))
                {
                    guided.DrawToBitmap(bitmap, new Rectangle(Point.Empty, guided.Size));
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                guided.Close();
            }
        }

        internal void SaveProjectForSelfTest(string path)
        {
            SaveProjectToPath(path, true);
        }

        internal void AllowCloseForSelfTest()
        {
            allowCloseWithoutPrompt = true;
        }

        internal bool RunLogicSelfTest()
        {
            WorkerRequest lifecycleRequest = new WorkerRequest { Operation = "scan", KeepSourceDocumentsOpen = true };
            WorkerResponse lifecycleResponse = new WorkerResponse { Success = true, SolidWorksKeptOpen = true };
            SourceRecord completedSource = new SourceRecord { Status = "读取完成" };
            SourceRecord failedSource = new SourceRecord { Status = "读取失败" };
            if (!WorkerMain.ShouldKeepScanSession(lifecycleRequest, lifecycleResponse) ||
                !WorkerMain.ShouldKeepScannedDocument(lifecycleRequest, completedSource) ||
                WorkerMain.ShouldKeepScannedDocument(lifecycleRequest, failedSource) ||
                WorkerMain.ShouldKeepScanSession(new WorkerRequest { Operation = "export", KeepSourceDocumentsOpen = true }, lifecycleResponse) ||
                !JsonFile.Clone(lifecycleRequest).KeepSourceDocumentsOpen ||
                !JsonFile.Clone(lifecycleResponse).SolidWorksKeptOpen ||
                !IsAutomaticSolidWorksConnectionFailure(new WorkerResponse { Success = false, Message = "SolidWorks 界面已启动，但 90 秒内未能连接自动化接口。" }) ||
                IsAutomaticSolidWorksConnectionFailure(new WorkerResponse { Success = false, Message = "源文件不存在。" })) return false;
            if (stepFolderCombo.SelectedIndex != (project.Export.SeparateStepOutput ? 1 : 0) || string.IsNullOrWhiteSpace(stepFolderCombo.Text)) return false;
            ExportSettings separateClone = JsonFile.Clone(new ExportSettings { SeparateStepOutput = true });
            if (!separateClone.SeparateStepOutput) return false;
            List<BodyRecord> all = project.AllBodies().ToList();
            if (all.Count < 2) return false;
            Dictionary<string, string> originalKeys = all.ToDictionary(body => body.Id, body => body.GeometryKey);
            bool originalDeduplicate = project.Export.Deduplicate;
            string firstName = all[0].ExportName;
            string firstCategory = all[0].CategoryId;
            bool firstSelected = all[0].ExportSelected;
            string secondName = all[1].ExportName;
            string secondCategory = all[1].CategoryId;
            bool secondSelected = all[1].ExportSelected;
            try
            {
                foreach (BodyRecord body in all) body.GeometryKey = "v120-unique-" + body.Id;
                all[0].GeometryKey = "v120-selftest-geometry";
                all[1].GeometryKey = "v120-selftest-geometry";
                project.Export.Deduplicate = true;
                if (GetDisplayBodies(string.Empty).Count != all.Count - 1) return false;
                ApplyToGroup(all[0], "V120_GROUP_TEST", CategoryNode.UnclassifiedId, false);
                return all[1].ExportName == "V120_GROUP_TEST" && all[1].CategoryId == CategoryNode.UnclassifiedId && !all[1].ExportSelected;
            }
            finally
            {
                foreach (BodyRecord body in all) body.GeometryKey = originalKeys[body.Id];
                all[0].ExportName = firstName;
                all[0].CategoryId = firstCategory;
                all[0].ExportSelected = firstSelected;
                all[1].ExportName = secondName;
                all[1].CategoryId = secondCategory;
                all[1].ExportSelected = secondSelected;
                project.Export.Deduplicate = originalDeduplicate;
            }
        }

        private void ApplyLanguage()
        {
            Text = "Master Miao · V1.2.4";
            UiText.Apply(this);
            if (conflictCombo.Items.Count > 0)
            {
                int policyIndex = conflictCombo.SelectedIndex < 0 ? 0 : conflictCombo.SelectedIndex;
                bool oldSuppress = suppressDirty;
                suppressDirty = true;
                conflictCombo.Items.Clear();
                conflictCombo.Items.AddRange(new object[] { UiText.T("跳过", "Skip"), UiText.T("自动编号", "Auto-number"), UiText.T("覆盖", "Overwrite") });
                conflictCombo.SelectedIndex = policyIndex;
                suppressDirty = oldSuppress;
            }
            if (stepFolderCombo.Items.Count > 0)
            {
                bool separate = project.Export != null && project.Export.SeparateStepOutput;
                bool oldSuppress = suppressDirty;
                suppressDirty = true;
                stepFolderCombo.Items.Clear();
                stepFolderCombo.Items.AddRange(new object[]
                {
                    UiText.T("与 SLDPRT 同目录", "Same folder as SLDPRT"),
                    UiText.T("独立双目录（镜像分类树）", "Separate mirrored folder trees")
                });
                stepFolderCombo.SelectedIndex = separate ? 1 : 0;
                suppressDirty = oldSuppress;
                UpdateStepFolderHint();
            }
            if (bodyGrid.Columns.Count > 0)
            {
                bodyGrid.Columns["Selected"].HeaderText = UiText.T("选", "Use");
                bodyGrid.Columns["ThumbnailIso"].HeaderText = UiText.T("等轴测", "Isometric");
                bodyGrid.Columns["ThumbnailFront"].HeaderText = UiText.T("前视图", "Front");
                bodyGrid.Columns["ThumbnailTop"].HeaderText = UiText.T("上视图", "Top");
                bodyGrid.Columns["OriginalName"].HeaderText = UiText.T("原实体名", "Original body");
                bodyGrid.Columns["ExportName"].HeaderText = UiText.T("导出名称", "Export name");
                bodyGrid.Columns["Category"].HeaderText = UiText.T("分类", "Category");
                bodyGrid.Columns["Quantity"].HeaderText = UiText.T("相同件", "Qty");
                bodyGrid.Columns["Status"].HeaderText = UiText.T("状态", "Status");
            }
            UpdateWindowTitle();
        }

        private void OpenSettings(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            using (LanguageDialog dialog = new LanguageDialog(false))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                dialog.ApplySelection();
                ApplyLanguage();
                RefreshSources();
                RefreshGrid();
                UpdateEnvironmentSummary();
                progressLabel.Text = UiText.T("语言设置已保存。", "Language setting saved.");
            }
        }

        private static string LocalWorkerStage(string stage)
        {
            if (!UiText.IsEnglish) return stage;
            switch (stage)
            {
                case "检测环境": return "Checking environment";
                case "读取文件": return "Reading files";
                case "生成预览": return "Generating previews";
                case "导出零件": return "Exporting parts";
                case "验证零件": return "Verifying parts";
                case "生成装配体": return "Creating assembly";
                case "准备 STEP": return "Preparing STEP";
                case "导出 STEP": return "Exporting STEP";
                case "检测到 SolidWorks 干扰": return "SolidWorks interference detected";
                case "完成": return "Complete";
                default: return stage;
            }
        }

        private void ZoomChanged(object sender, EventArgs e)
        {
            if (zoomCombo.SelectedItem == null) return;
            int value;
            if (!int.TryParse(Convert.ToString(zoomCombo.SelectedItem).TrimEnd('%'), out value)) return;
            ApplyListZoom(value);
            if (!suppressDirty)
            {
                project.ListZoomPercent = value;
                UserSettingsStore.Current.ListZoomPercent = value;
                UserSettingsStore.Save();
                MarkProjectDirty(false);
            }
        }

        private void BodyGridMouseWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != Keys.Control) return;
            int[] values = { 80, 100, 125, 150, 175, 200 };
            int current = project.ListZoomPercent < 80 ? 100 : project.ListZoomPercent;
            int index = Array.IndexOf(values, current);
            if (index < 0) index = 1;
            index = Math.Max(0, Math.Min(values.Length - 1, index + (e.Delta > 0 ? 1 : -1)));
            zoomCombo.SelectedItem = values[index] + "%";
        }

        private void ApplyListZoom(int percent)
        {
            int[] values = { 80, 100, 125, 150, 175, 200 };
            int nearest = values.OrderBy(value => Math.Abs(value - percent)).First();
            float scale = nearest / 100F;
            project.ListZoomPercent = nearest;
            bodyGrid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", Math.Max(7.2F, 9F * scale));
            bodyGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", Math.Max(7.2F, 9F * scale), FontStyle.Bold);
            bodyGrid.RowTemplate.Height = Math.Max(62, (int)Math.Round(78 * scale));
            bodyGrid.ColumnHeadersHeight = Math.Max(29, (int)Math.Round(34 * scale));
            SetColumnWidth("Selected", 42, scale);
            SetColumnWidth("ThumbnailIso", 92, scale);
            SetColumnWidth("ThumbnailFront", 92, scale);
            SetColumnWidth("ThumbnailTop", 92, scale);
            SetColumnWidth("OriginalName", 112, scale);
            SetColumnWidth("ExportName", 142, scale);
            SetColumnWidth("Category", 160, scale);
            SetColumnWidth("Quantity", 68, scale);
            SetColumnWidth("Status", 92, scale);
            foreach (DataGridViewRow row in bodyGrid.Rows) row.Height = bodyGrid.RowTemplate.Height;
            string text = nearest + "%";
            if (!string.Equals(Convert.ToString(zoomCombo.SelectedItem), text, StringComparison.Ordinal))
            {
                bool old = suppressDirty;
                suppressDirty = true;
                int itemIndex = Array.IndexOf(values, nearest);
                if (itemIndex >= 0 && itemIndex < zoomCombo.Items.Count) zoomCombo.SelectedIndex = itemIndex;
                suppressDirty = old;
            }
        }

        private void SetColumnWidth(string name, int baseWidth, float scale)
        {
            DataGridViewColumn column = bodyGrid.Columns[name];
            if (column != null) column.Width = Math.Max(column.MinimumWidth, (int)Math.Round(baseWidth * scale));
        }

        private List<BodyRecord> GetDisplayBodies(string sourceFilter)
        {
            List<BodyRecord> all = project.AllBodies().ToList();
            if (!project.Export.Deduplicate)
                return string.IsNullOrWhiteSpace(sourceFilter) ? all : all.Where(item => item.SourceId == sourceFilter).ToList();
            return all.GroupBy(GeometryGroupKey)
                .Where(group => string.IsNullOrWhiteSpace(sourceFilter) || group.Any(item => item.SourceId == sourceFilter))
                .Select(group => group.First())
                .ToList();
        }

        private List<BodyRecord> GetGroupMembers(BodyRecord body)
        {
            if (body == null) return new List<BodyRecord>();
            if (!project.Export.Deduplicate) return new List<BodyRecord> { body };
            string key = GeometryGroupKey(body);
            return project.AllBodies().Where(item => string.Equals(GeometryGroupKey(item), key, StringComparison.Ordinal)).ToList();
        }

        private static string GeometryGroupKey(BodyRecord body)
        {
            return string.IsNullOrWhiteSpace(body.GeometryKey) ? "id:" + body.Id : "geometry:" + body.GeometryKey;
        }

        private void ApplyToGroup(BodyRecord body, string exportName, string categoryId, bool selected)
        {
            foreach (BodyRecord member in GetGroupMembers(body))
            {
                member.ExportName = exportName;
                member.CategoryId = categoryId;
                member.ExportSelected = selected;
            }
        }

        private string DisplayCategoryPath(string categoryId)
        {
            string path = CategoryRules.GetPath(project.Categories, categoryId);
            if (UiText.IsEnglish && string.Equals(categoryId, CategoryNode.UnclassifiedId, StringComparison.Ordinal)) return "Unclassified";
            return path;
        }

        private void BatchCategorize(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            List<BodyRecord> selected = bodyGrid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.Tag as BodyRecord).Where(item => item != null).Distinct().ToList();
            if (selected.Count == 0 && bodyGrid.CurrentRow != null)
            {
                BodyRecord current = bodyGrid.CurrentRow.Tag as BodyRecord;
                if (current != null) selected.Add(current);
            }
            if (selected.Count == 0)
            {
                MessageBox.Show(this, UiText.T("请先在列表中选择一个或多个实体行。", "Select one or more body rows first."), UiText.T("批量分类", "Batch category"));
                return;
            }
            using (CategoryChoiceDialog dialog = new CategoryChoiceDialog(project.Categories))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (BodyRecord body in selected)
                    foreach (BodyRecord member in GetGroupMembers(body)) member.CategoryId = dialog.CategoryId;
                MarkProjectDirty();
                RefreshGrid();
            }
        }

        private void OpenGuidedMode(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            CommitGrid();
            List<BodyRecord> bodies = GetDisplayBodies(string.Empty);
            if (bodies.Count == 0)
            {
                MessageBox.Show(this, UiText.T("当前没有可整理的实体。", "There are no bodies to organize."), UiText.T("逐项整理", "Guided mode"));
                return;
            }
            using (GuidedBodyForm dialog = new GuidedBodyForm(project, bodies, GetGroupMembers,
                delegate { MarkProjectDirty(); }, LocateBodiesInSolidWorks))
                dialog.ShowDialog(this);
            MarkProjectDirty(false);
            RefreshGrid();
        }

        private void LocateSelectedBodies(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            List<BodyRecord> selected = bodyGrid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.Tag as BodyRecord).Where(item => item != null).Distinct().ToList();
            if (selected.Count == 0 && bodyGrid.CurrentRow != null)
            {
                BodyRecord current = bodyGrid.CurrentRow.Tag as BodyRecord;
                if (current != null) selected.Add(current);
            }
            string message = LocateBodiesInSolidWorks(selected);
            if (!string.IsNullOrWhiteSpace(message))
                MessageBox.Show(this, message, UiText.T("SolidWorks 实体定位", "Locate body in SolidWorks"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private string LocateBodiesInSolidWorks(IList<BodyRecord> bodies)
        {
            if (worker.IsBusy) return UiText.T("读取或导出期间不能执行定位。", "Body location is disabled while scanning or exporting.");
            string error;
            if (!SolidWorksLocator.Highlight(bodies, out error)) return error;
            progressLabel.Text = string.Format(UiText.T("已在 SolidWorks 中高亮 {0} 个实体。", "Highlighted {0} body/bodies in SolidWorks."), bodies.Count);
            return string.Empty;
        }

        private void DisposeGridImages()
        {
            string[] names = { "ThumbnailIso", "ThumbnailFront", "ThumbnailTop" };
            foreach (DataGridViewRow row in bodyGrid.Rows)
                foreach (string name in names)
                {
                    DataGridViewCell cell = bodyGrid.Columns.Contains(name) ? row.Cells[name] : null;
                    Image image = cell == null ? null : cell.Value as Image;
                    if (image != null) { cell.Value = null; image.Dispose(); }
                }
        }

        private bool ValidateOutputRoot(string path, out string error)
        {
            error = string.Empty;
            try
            {
                string full = Path.GetFullPath(path);
                Directory.CreateDirectory(full);
                string probe = Path.Combine(full, ".swbo_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "write test");
                File.Delete(probe);
                string root = Path.GetPathRoot(full);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    DriveInfo drive = new DriveInfo(root);
                    if (drive.IsReady && drive.AvailableFreeSpace < 100L * 1024L * 1024L)
                    {
                        error = UiText.T("输出磁盘可用空间不足 100 MB，请更换输出位置。", "The output drive has less than 100 MB free. Choose another location.");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = UiText.T("输出位置不可写：\n", "The output location is not writable:\n") + ex.Message;
                return false;
            }
        }

        private void MarkProjectDirty() { MarkProjectDirty(true); }

        private void MarkProjectDirty(bool invalidateExport)
        {
            if (suppressDirty) return;
            projectDirty = true;
            if (invalidateExport) project.LastExportSucceeded = false;
            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            string name = string.IsNullOrWhiteSpace(project.Name) ? UiText.T("未命名项目", "Untitled project") : project.Name;
            Text = "Master Miao · V1.2.4 · " + name + (projectDirty ? " *" : string.Empty);
        }

        private bool SaveProjectInteractive()
        {
            if (worker.IsBusy) return false;
            CommitGrid();
            if (string.IsNullOrWhiteSpace(currentProjectPath))
            {
                string parent;
                using (FolderBrowserDialog folder = new FolderBrowserDialog
                {
                    Description = UiText.T("选择用于保存工作项目文件夹的位置。程序会在这里创建 project.swbody.json 和 Previews 文件夹。", "Choose where to create the work project folder containing project.swbody.json and Previews."),
                    SelectedPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory)
                })
                {
                    if (folder.ShowDialog(this) != DialogResult.OK) return false;
                    parent = folder.SelectedPath;
                }
                using (TextPrompt nameDialog = new TextPrompt(UiText.T("保存工作项目", "Save work project"), UiText.T("项目名称", "Project name"), project.Name))
                {
                    if (nameDialog.ShowDialog(this) != DialogResult.OK) return false;
                    project.Name = NameRules.SafeStem(nameDialog.Value, UiText.T("实体分类项目", "Body classification project"));
                }
                string projectFolder = Path.Combine(parent, NameRules.SafeStem(project.Name, "SWBO_Project") + "_SWBO项目");
                currentProjectPath = Path.Combine(projectFolder, "project.swbody.json");
                if (File.Exists(currentProjectPath) && MessageBox.Show(this,
                    UiText.T("该工作项目已经存在，是否覆盖项目记录？导出的零件文件不会被删除。", "This work project already exists. Overwrite its project record? Exported parts will not be deleted."),
                    UiText.T("项目已存在", "Project exists"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    currentProjectPath = string.Empty;
                    return false;
                }
            }
            try
            {
                SaveProjectToPath(currentProjectPath, true);
                progressLabel.Text = UiText.T("项目已保存：", "Project saved: ") + currentProjectPath;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, UiText.T("项目无法保存", "Could not save project"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void SaveProjectToPath(string path, bool copyPreviews)
        {
            if (copyPreviews) CopyPreviewsIntoProject(Path.GetDirectoryName(path));
            project.SchemaVersion = 2;
            project.LastSavedUtc = DateTime.UtcNow;
            JsonFile.Save(path, project);
            currentProjectPath = path;
            projectDirty = false;
            UserSettingsStore.RememberProject(path);
            DeleteRecoveryFile();
            UpdateWindowTitle();
        }

        private void SaveProjectSilently()
        {
            try
            {
                CommitGrid();
                if (!string.IsNullOrWhiteSpace(currentProjectPath)) SaveProjectToPath(currentProjectPath, true);
                else
                {
                    project.SchemaVersion = 2;
                    project.LastSavedUtc = DateTime.UtcNow;
                    JsonFile.Save(Path.Combine(AppPaths.Recovery, "autosave.swbody.json"), project);
                }
            }
            catch { }
        }

        private void CopyPreviewsIntoProject(string projectFolder)
        {
            if (string.IsNullOrWhiteSpace(projectFolder)) return;
            string previewRoot = Path.Combine(projectFolder, "Previews");
            Directory.CreateDirectory(previewRoot);
            foreach (BodyRecord body in project.AllBodies())
            {
                string bodyFolder = Path.Combine(previewRoot, NameRules.SafeStem(body.SourceId, "source"));
                Directory.CreateDirectory(bodyFolder);
                body.PreviewIso = CopyPreview(body.PreviewIso, Path.Combine(bodyFolder, string.Format("{0:D4}_iso.png", body.Index + 1)));
                body.PreviewFront = CopyPreview(body.PreviewFront, Path.Combine(bodyFolder, string.Format("{0:D4}_front.png", body.Index + 1)));
                body.PreviewTop = CopyPreview(body.PreviewTop, Path.Combine(bodyFolder, string.Format("{0:D4}_top.png", body.Index + 1)));
            }
        }

        private static string CopyPreview(string source, string target)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return source ?? string.Empty;
            string from = Path.GetFullPath(source);
            string to = Path.GetFullPath(target);
            if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) File.Copy(from, to, true);
            return to;
        }

        private void OpenProjectFile(string path)
        {
            if (projectDirty)
            {
                DialogResult choice = MessageBox.Show(this,
                    UiText.T("当前工作有未保存的修改。\n\n是：先保存\n否：放弃修改并打开\n取消：留在当前项目", "The current project has unsaved changes.\n\nYes: save first\nNo: discard and open\nCancel: stay here"),
                    UiText.T("打开项目", "Open project"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel || (choice == DialogResult.Yes && !SaveProjectInteractive())) return;
            }
            project = JsonFile.Load<AppProject>(path);
            currentProjectPath = Path.GetFullPath(path);
            projectDirty = false;
            UserSettingsStore.RememberProject(currentProjectPath);
            BindProject();
            UpdateWindowTitle();
            CheckProjectSources();
        }

        private void CheckProjectSources()
        {
            List<SourceRecord> missing = project.Sources.Where(source => !File.Exists(source.Path)).ToList();
            foreach (SourceRecord source in missing)
            {
                DialogResult answer = MessageBox.Show(this,
                    string.Format(UiText.T("找不到源文件：\n{0}\n\n是否现在重新指定它的位置？", "Source file not found:\n{0}\n\nLocate it now?"), source.Path),
                    UiText.T("源文件需要重新绑定", "Source file must be relinked"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) continue;
                using (OpenFileDialog dialog = new OpenFileDialog { Filter = "SolidWorks Part (*.SLDPRT)|*.SLDPRT", FileName = source.Name })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        source.Path = Path.GetFullPath(dialog.FileName);
                        source.Name = Path.GetFileName(source.Path);
                        foreach (BodyRecord body in source.Bodies) { body.SourcePath = source.Path; body.SourceName = source.Name; }
                        MarkProjectDirty();
                    }
            }
            List<SourceRecord> changed = project.Sources.Where(source => File.Exists(source.Path) && source.Length > 0 &&
                (new FileInfo(source.Path).Length != source.Length || new FileInfo(source.Path).LastWriteTimeUtc.Ticks != source.LastWriteTicks)).ToList();
            if (changed.Count > 0)
                MessageBox.Show(this,
                    string.Format(UiText.T("有 {0} 个源文件自上次读取后发生变化。导出前请点击“重新读取”。", "{0} source file(s) changed since the last scan. Click Rescan before exporting."), changed.Count),
                    UiText.T("源文件已变化", "Source files changed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void CheckRecoveryProject()
        {
            string recovery = Path.Combine(AppPaths.Recovery, "autosave.swbody.json");
            if (!File.Exists(recovery) || project.Sources.Count > 0) return;
            try
            {
                AppProject saved = JsonFile.Load<AppProject>(recovery);
                if (saved.Sources == null || saved.Sources.Count == 0) return;
                if (MessageBox.Show(this,
                    UiText.T("检测到上次未完成工作的自动恢复记录，是否继续？", "An autosaved unfinished project was found. Resume it?"),
                    UiText.T("恢复工作", "Resume work"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                project = saved;
                currentProjectPath = string.Empty;
                projectDirty = true;
                BindProject();
                UpdateWindowTitle();
                CheckProjectSources();
            }
            catch { }
        }

        private static void DeleteRecoveryFile()
        {
            try
            {
                string path = Path.Combine(AppPaths.Recovery, "autosave.swbody.json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private void HandleMainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (Program.SuppressStartupPrompts || allowCloseWithoutPrompt) return;
            if (worker.IsBusy)
            {
                e.Cancel = true;
                if (closeWhenIdle) return;
                if (MessageBox.Show(this,
                    UiText.T("SolidWorks 工作进程仍在运行。要发送取消请求并在安全结束后退出吗？", "A SolidWorks task is still running. Request a safe cancellation and exit when it finishes?"),
                    UiText.T("任务进行中", "Task in progress"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    closeWhenIdle = true;
                    RequestCancel();
                }
                return;
            }
            if (closeWhenIdle) { allowCloseWithoutPrompt = true; return; }
            int total = project.AllBodies().Count();
            int classified = project.AllBodies().Count(body => body.CategoryId != CategoryNode.UnclassifiedId);
            string summary = string.Format(UiText.T("当前进度：已分类 {0} / {1}，未分类 {2}。\n最近导出状态：{3}。", "Progress: classified {0} / {1}, unclassified {2}.\nLatest export: {3}."),
                classified, total, total - classified, project.LastExportSucceeded ? UiText.T("成功", "successful") : UiText.T("未完成或已修改", "incomplete or changed"));
            if (projectDirty || !project.LastExportSucceeded)
            {
                DialogResult choice = MessageBox.Show(this,
                    summary + UiText.T("\n\n工作是否已完成？\n是：保存项目并关闭\n否：不保存并直接关闭\n取消：返回程序", "\n\nIs the work complete?\nYes: save the project and close\nNo: close without saving\nCancel: return to the app"),
                    UiText.T("确认关闭", "Confirm close"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel || (choice == DialogResult.Yes && !SaveProjectInteractive())) { e.Cancel = true; return; }
            }
            else if (MessageBox.Show(this, summary + UiText.T("\n\n确认关闭软件？", "\n\nClose the application?"), UiText.T("确认关闭", "Confirm close"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            { e.Cancel = true; return; }
            allowCloseWithoutPrompt = true;
        }
    }

    internal sealed class CategoryChoiceDialog : Form
    {
        private readonly ComboBox categories = new ComboBox();
        public string CategoryId { get { return Convert.ToString(categories.SelectedValue) ?? CategoryNode.UnclassifiedId; } }

        public CategoryChoiceDialog(List<CategoryNode> nodes)
        {
            UiBrand.ApplyIcon(this);
            Text = UiText.T("批量分类", "Batch category");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 150);
            Font = new Font("Microsoft YaHei UI", 9F);
            Controls.Add(new Label { Text = UiText.T("选择要应用到所选实体的标签 / 文件夹", "Choose the category folder for the selected bodies"), Left = 20, Top = 18, Width = 385, Height = 24 });
            categories.Left = 20; categories.Top = 51; categories.Width = 385; categories.DropDownStyle = ComboBoxStyle.DropDownList;
            categories.DataSource = CategoryRules.BuildOptions(nodes); categories.DisplayMember = "Path"; categories.ValueMember = "Id";
            Button ok = new Button { Text = UiText.T("应用", "Apply"), DialogResult = DialogResult.OK, Left = 235, Top = 100, Width = 80 };
            Button cancel = new Button { Text = UiText.T("取消", "Cancel"), DialogResult = DialogResult.Cancel, Left = 325, Top = 100, Width = 80 };
            Controls.AddRange(new Control[] { categories, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    internal sealed class GuidedBodyForm : Form
    {
        private readonly AppProject project;
        private readonly IList<BodyRecord> bodies;
        private readonly Func<BodyRecord, List<BodyRecord>> groupProvider;
        private readonly Action changed;
        private readonly Func<IList<BodyRecord>, string> locator;
        private readonly PictureBox iso = CreateView();
        private readonly PictureBox front = CreateView();
        private readonly PictureBox top = CreateView();
        private readonly TextBox exportName = new TextBox();
        private readonly ComboBox category = new ComboBox();
        private readonly CheckBox selected = new CheckBox();
        private readonly Label progress = new Label();
        private readonly Label details = new Label();
        private readonly TextBox output = new TextBox();
        private int index;
        private bool loading;

        public GuidedBodyForm(AppProject project, IList<BodyRecord> bodies, Func<BodyRecord, List<BodyRecord>> groupProvider, Action changed, Func<IList<BodyRecord>, string> locator)
        {
            UiBrand.ApplyIcon(this);
            this.project = project; this.bodies = bodies; this.groupProvider = groupProvider; this.changed = changed; this.locator = locator;
            index = Math.Max(0, Math.Min(bodies.Count - 1, project.GuidedIndex));
            Text = UiText.T("逐项整理", "Guided body organizer");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(940, 650);
            Size = new Size(1240, 820);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(244, 246, 248);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            progress.Dock = DockStyle.Fill; progress.Font = new Font(Font, FontStyle.Bold); progress.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(progress, 0, 0);

            TableLayoutPanel views = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F)); views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F)); views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
            views.Controls.Add(Wrap(UiText.T("等轴测", "Isometric"), iso), 0, 0);
            views.Controls.Add(Wrap(UiText.T("前视图", "Front"), front), 1, 0);
            views.Controls.Add(Wrap(UiText.T("上视图", "Top"), top), 2, 0);
            root.Controls.Add(views, 0, 1);

            TableLayoutPanel editor = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12), ColumnCount = 4, RowCount = 3 };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            editor.Controls.Add(LabelFor(UiText.T("导出名称", "Export name")), 0, 0); exportName.Dock = DockStyle.Fill; editor.Controls.Add(exportName, 1, 0);
            editor.Controls.Add(LabelFor(UiText.T("标签 / 文件夹", "Category folder")), 2, 0); category.Dock = DockStyle.Fill; category.DropDownStyle = ComboBoxStyle.DropDownList; category.DataSource = CategoryRules.BuildOptions(project.Categories); category.DisplayMember = "Path"; category.ValueMember = "Id"; editor.Controls.Add(category, 3, 0);
            editor.Controls.Add(LabelFor(UiText.T("输出位置", "Output location")), 0, 1); output.Dock = DockStyle.Fill; output.Text = project.OutputRoot; editor.Controls.Add(output, 1, 1); editor.SetColumnSpan(output, 2);
            Button browse = new Button { Text = UiText.T("选择…", "Browse…"), Dock = DockStyle.Fill }; browse.Click += ChooseOutput; editor.Controls.Add(browse, 3, 1);
            selected.Text = UiText.T("选择此零件组用于导出", "Include this part group in export"); selected.Dock = DockStyle.Fill; editor.Controls.Add(selected, 0, 2); editor.SetColumnSpan(selected, 2);
            details.Dock = DockStyle.Fill; details.ForeColor = Color.FromArgb(90, 96, 106); details.AutoEllipsis = true; editor.Controls.Add(details, 2, 2); editor.SetColumnSpan(details, 2);
            root.Controls.Add(editor, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
            Button close = Make(UiText.T("返回列表", "Back to list"), delegate { Commit(); DialogResult = DialogResult.OK; Close(); });
            Button next = Make(UiText.T("保存并下一个", "Save and next"), delegate { Commit(); MoveIndex(1); });
            Button previous = Make(UiText.T("上一个", "Previous"), delegate { Commit(); MoveIndex(-1); });
            Button skip = Make(UiText.T("暂不分类", "Skip for now"), delegate { Commit(false); MoveIndex(1); });
            Button locate = Make(UiText.T("在 SW 中定位", "Locate in SW"), Locate);
            buttons.Controls.Add(close); buttons.Controls.Add(next); buttons.Controls.Add(previous); buttons.Controls.Add(skip); buttons.Controls.Add(locate);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);
            FormClosing += delegate { Commit(); DisposeViews(); project.GuidedIndex = index; };
            LoadCurrent();
        }

        private void LoadCurrent()
        {
            loading = true;
            BodyRecord body = bodies[index];
            SetView(iso, body.PreviewIso); SetView(front, body.PreviewFront); SetView(top, body.PreviewTop);
            exportName.Text = body.ExportName; category.SelectedValue = body.CategoryId; selected.Checked = body.ExportSelected;
            List<BodyRecord> group = groupProvider(body);
            int classified = bodies.Count(item => item.CategoryId != CategoryNode.UnclassifiedId);
            progress.Text = string.Format(UiText.T("零件 {0} / {1}    已分类 {2}    未分类 {3}", "Part {0} / {1}    Classified {2}    Unclassified {3}"), index + 1, bodies.Count, classified, bodies.Count - classified);
            details.Text = string.Format(UiText.T("来源：{0} · 原实体：{1} · 相同件：{2}", "Source: {0} · Original: {1} · Identical: {2}"), body.SourceName, body.OriginalName, group.Count);
            loading = false;
        }

        private void Commit() { Commit(true); }

        private void Commit(bool saveCategory)
        {
            if (loading || bodies.Count == 0) return;
            BodyRecord body = bodies[index];
            string name = NameRules.SafeStem(exportName.Text, "Part_" + (body.Index + 1));
            string categoryId = saveCategory ? (Convert.ToString(category.SelectedValue) ?? CategoryNode.UnclassifiedId) : body.CategoryId;
            foreach (BodyRecord member in groupProvider(body))
            {
                member.ExportName = name; member.CategoryId = categoryId; member.ExportSelected = selected.Checked;
            }
            project.OutputRoot = output.Text.Trim();
            project.GuidedIndex = index;
            changed();
        }

        private void MoveIndex(int delta)
        {
            index = Math.Max(0, Math.Min(bodies.Count - 1, index + delta));
            project.GuidedIndex = index;
            LoadCurrent();
        }

        private void Locate(object sender, EventArgs e)
        {
            Commit();
            string error = locator(new List<BodyRecord> { bodies[index] });
            if (!string.IsNullOrWhiteSpace(error)) MessageBox.Show(this, error, UiText.T("SolidWorks 实体定位", "Locate body in SolidWorks"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ChooseOutput(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = UiText.T("选择最终导出文件夹", "Choose the final export folder"), SelectedPath = Directory.Exists(output.Text) ? output.Text : System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory) })
                if (dialog.ShowDialog(this) == DialogResult.OK) { output.Text = dialog.SelectedPath; project.OutputRoot = output.Text; changed(); }
        }

        private void DisposeViews()
        {
            foreach (PictureBox box in new[] { iso, front, top }) { Image image = box.Image; box.Image = null; if (image != null) image.Dispose(); }
        }

        private static PictureBox CreateView() { return new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom }; }
        private static GroupBox Wrap(string title, Control content) { GroupBox box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(6) }; box.Controls.Add(content); return box; }
        private static Label LabelFor(string text) { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(70, 76, 86) }; }
        private static Button Make(string text, EventHandler action) { Button button = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(6, 0, 0, 0) }; button.Click += action; return button; }
        private static void SetView(PictureBox box, string path) { Image old = box.Image; box.Image = LoadImageFile(path); if (old != null) old.Dispose(); }
        private static Image LoadImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try { using (Image source = Image.FromFile(path)) return new Bitmap(source); } catch { return null; }
        }
    }

    internal static class SolidWorksLocator
    {
        public static bool Highlight(IList<BodyRecord> requested, out string error)
        {
            error = string.Empty;
            List<BodyRecord> bodies = requested == null ? new List<BodyRecord>() : requested.Where(item => item != null).ToList();
            if (bodies.Count == 0) { error = UiText.T("没有选中实体。", "No body is selected."); return false; }
            if (bodies.Select(item => item.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            { error = UiText.T("一次只能在同一个源文件中定位多个实体。", "Multiple highlighted bodies must come from the same source file."); return false; }
            if (Process.GetProcessesByName("SLDWORKS").Length == 0)
            { error = UiText.T("SolidWorks 尚未运行。请先在 SolidWorks 中打开该多实体源文件。", "SolidWorks is not running. Open the multi-body source file in SolidWorks first."); return false; }

            ISldWorks app = null;
            IModelDoc2 model = null;
            IPartDoc part = null;
            object[] swBodies = null;
            try
            {
                app = Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                if (app == null) throw new InvalidOperationException(UiText.T("无法连接当前 SolidWorks 会话。", "Could not connect to the current SolidWorks session."));
                model = app.GetOpenDocumentByName(bodies[0].SourcePath) as IModelDoc2;
                if (model == null)
                {
                    IModelDoc2 active = app.ActiveDoc as IModelDoc2;
                    if (active != null && string.Equals(active.GetPathName(), bodies[0].SourcePath, StringComparison.OrdinalIgnoreCase)) model = active;
                    else Release(active);
                }
                if (model == null)
                { error = UiText.T("源文件没有在当前 SolidWorks 会话中打开：\n", "The source file is not open in the current SolidWorks session:\n") + bodies[0].SourcePath; return false; }
                part = model as IPartDoc;
                if (part == null) { error = UiText.T("当前文档不是 SolidWorks 零件。", "The open document is not a SolidWorks part."); return false; }
                int activateError = 0;
                app.ActivateDoc3(model.GetTitle(), false, 0, ref activateError);
                swBodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];
                model.ClearSelection2(true);
                bool append = false;
                foreach (BodyRecord requestedBody in bodies)
                {
                    IBody2 match = null;
                    if (requestedBody.Index >= 0 && requestedBody.Index < swBodies.Length)
                    {
                        IBody2 indexed = swBodies[requestedBody.Index] as IBody2;
                        if (indexed != null && string.Equals(indexed.Name, requestedBody.OriginalName, StringComparison.Ordinal)) match = indexed;
                    }
                    if (match == null)
                    {
                        List<IBody2> named = swBodies.Select(item => item as IBody2).Where(item => item != null && string.Equals(item.Name, requestedBody.OriginalName, StringComparison.Ordinal)).ToList();
                        if (named.Count == 1) match = named[0];
                    }
                    if (match == null)
                    { error = string.Format(UiText.T("无法可靠匹配实体“{0}”。源文件可能已经变化，请重新读取。", "Could not reliably match body '{0}'. The source may have changed; rescan it."), requestedBody.OriginalName); return false; }
                    if (!match.Select2(append, null))
                    { error = string.Format(UiText.T("SolidWorks 未能选择实体“{0}”。", "SolidWorks could not select body '{0}'."), requestedBody.OriginalName); return false; }
                    append = true;
                }
                model.ViewZoomToSelection();
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally
            {
                if (swBodies != null) foreach (object item in swBodies) Release(item);
                Release(part); Release(model); Release(app);
            }
        }

        private static void Release(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }
}
