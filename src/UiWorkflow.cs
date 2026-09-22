using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SWBodyOrganizer
{
    public sealed partial class MainForm
    {
        private readonly TextBox bodySearch = new TextBox();
        private readonly ComboBox bodyFilter = new ComboBox();
        private readonly CheckBox compactList = new CheckBox();
        private readonly Button undoButton = new Button();
        private readonly Button retryFailedButton = new Button();
        private readonly Stack<EditUndoState> undoHistory = new Stack<EditUndoState>();
        private readonly Dictionary<string, Image> thumbnailCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> candidateBodyIds = new HashSet<string>();
        private bool changingOptions;
        private WorkerRequest lastExportRequest;
        private WorkerResponse lastExportResponse;
        private HashSet<string> retryBodyIds;
        private DateTime lastWorkerProgressUtc;
        private bool cancellationRequested;
        private readonly Timer workerWatchdog = new Timer();
        private readonly Panel issueNavigator = new Panel { Dock = DockStyle.Top, Height = 84, Visible = false, BackColor = Color.FromArgb(255, 244, 224) };
        private readonly Label issueLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(8, 0, 4, 0) };
        private readonly List<ReviewIssue> validationIssues = new List<ReviewIssue>();
        private List<ReviewIssue> reviewIssues = new List<ReviewIssue>();
        private int reviewIndex;
        private bool reviewPreflight;
        private string issueFocusBodyId = string.Empty;
        private ExportProgressDialog exportProgressDialog;

        private Control BuildIssueNavigator()
        {
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, WrapContents = false, Padding = new Padding(6, 2, 0, 0) };
            buttons.Controls.Add(MakeSmallButton(UiText.T("上一项", "Previous issue"), delegate { MoveReview(-1); }));
            buttons.Controls.Add(MakeSmallButton(UiText.T("检查并下一项", "Check / next issue"), delegate { MoveReview(1); }));
            buttons.Controls.Add(MakeSmallButton(UiText.T("收起", "Dismiss"), delegate { CommitGrid(); DismissIssues(); }));
            issueNavigator.Controls.Add(issueLabel); issueNavigator.Controls.Add(buttons);
            return issueNavigator;
        }

        private void AddValidationIssues(IEnumerable<BodyRecord> bodies, string column, string message)
        {
            foreach (BodyRecord body in bodies)
                if (!validationIssues.Any(issue => issue.BodyId == body.Id))
                    validationIssues.Add(new ReviewIssue { BodyId = body.Id, SourceId = body.SourceId, Column = column, Message = message });
        }

        private List<ReviewIssue> OrderedIssues(IEnumerable<ReviewIssue> issues)
        {
            List<string> order = project.AllBodies().Select(body => body.Id).ToList();
            return issues.OrderBy(issue => project.Sources.FindIndex(source => source.Id == issue.SourceId))
                .ThenBy(issue => order.IndexOf(issue.BodyId)).ToList();
        }

        private void ShowValidationIssues()
        {
            reviewPreflight = true; reviewIssues = OrderedIssues(validationIssues); reviewIndex = 0;
            if (reviewIssues.Count > 0) FocusReviewIssue();
            else { DismissIssues(); outputBox.Focus(); }
        }

        private void ShowFailedItems(WorkerResponse response)
        {
            reviewPreflight = false;
            reviewIssues = OrderedIssues((response.ExportResults ?? new List<ExportResultItem>()).Where(IsFailedResult)
                .Select(item => new { Result = item, Body = project.AllBodies().FirstOrDefault(body => body.Id == item.BodyId) })
                .Where(item => item.Body != null).Select(item => new ReviewIssue { BodyId = item.Body.Id, SourceId = item.Body.SourceId,
                    Column = "Status", Message = UiText.T("导出未完成：", "Export incomplete: ") + item.Result.Message }));
            reviewIndex = 0; if (reviewIssues.Count > 0) FocusReviewIssue();
        }

        private void ShowScanIssues(WorkerResponse response)
        {
            reviewPreflight = false; reviewIssues.Clear();
            foreach (SourceRecord failed in (response.Sources ?? new List<SourceRecord>()).Where(source => source.Status != "读取完成"))
            {
                SourceRecord source = project.Sources.FirstOrDefault(item => item.Id == failed.Id);
                if (source == null) continue;
                BodyRecord body = source.Bodies.FirstOrDefault();
                reviewIssues.Add(new ReviewIssue { BodyId = body == null ? string.Empty : body.Id, SourceId = source.Id,
                    Column = "Status", Message = UiText.T("读取未完成：", "Scan incomplete: ") + source.Name + " / " + failed.Message });
            }
            reviewIssues = OrderedIssues(reviewIssues); reviewIndex = 0;
            if (reviewIssues.Count > 0) FocusReviewIssue();
        }

        private void MoveReview(int direction)
        {
            if (worker.IsBusy || reviewIssues.Count == 0) return;
            CommitGrid();
            List<string> order = project.AllBodies().Select(body => body.Id).ToList();
            int previous = order.IndexOf(reviewIssues[reviewIndex].BodyId);
            if (reviewPreflight)
            {
                string error; BuildExportPlan(out error);
                reviewIssues = OrderedIssues(validationIssues);
                if (reviewIssues.Count == 0)
                {
                    DismissIssues();
                    progressLabel.Text = string.IsNullOrWhiteSpace(error) ? UiText.T("条目检查通过。确认后可重新点击导出。", "Item checks passed. Review your choices and click Export again.") : error;
                    return;
                }
                reviewIndex = direction > 0 ? reviewIssues.FindIndex(issue => order.IndexOf(issue.BodyId) > previous)
                    : reviewIssues.FindLastIndex(issue => order.IndexOf(issue.BodyId) < previous);
                if (reviewIndex < 0) reviewIndex = direction > 0 ? 0 : reviewIssues.Count - 1;
            }
            else reviewIndex = Math.Max(0, Math.Min(reviewIssues.Count - 1, reviewIndex + direction));
            FocusReviewIssue();
        }

        private void FocusReviewIssue()
        {
            if (reviewIssues.Count == 0 || worker.IsBusy) return;
            CommitGrid();
            ReviewIssue issue = reviewIssues[reviewIndex]; issueFocusBodyId = issue.BodyId;
            gridRefreshing = true; bodySearch.Clear(); bodyFilter.SelectedIndex = 0; gridRefreshing = false;
            sourceSearchBox.Clear(); RefreshSources();
            if (sourceList.Items.Count > 0) sourceList.SelectedIndex = 0;
            RefreshGrid();
            DataGridViewRow row = bodyGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(value => ((BodyRecord)value.Tag).Id == issue.BodyId);
            if (row != null)
            {
                bodyGrid.ClearSelection();
                string column = bodyGrid.Columns.Contains(issue.Column) && bodyGrid.Columns[issue.Column].Visible ? issue.Column : "ExportName";
                bodyGrid.CurrentCell = row.Cells[column]; row.Selected = true;
                bodyGrid.FirstDisplayedScrollingRowIndex = row.Index;
                bodyGrid.Focus();
                ShowSelectedPreviews();
            }
            else
                for (int i = 0; i < sourceList.Items.Count; i++) if (((SourceListItem)sourceList.Items[i]).Id == issue.SourceId) { sourceList.SelectedIndex = i; sourceList.Focus(); break; }
            issueLabel.Text = string.Format(UiText.T("待处理 {0}/{1}：{2}", "Issue {0}/{1}: {2}"), reviewIndex + 1, reviewIssues.Count, issue.Message);
            toolTip.SetToolTip(issueLabel, issue.Message); issueNavigator.Visible = true;
        }

        private void DismissIssues()
        {
            issueFocusBodyId = string.Empty; reviewIssues.Clear(); issueNavigator.Visible = false;
        }

        private void ShowExportProgress()
        {
            using (ExportProgressDialog dialog = new ExportProgressDialog(delegate { RequestCancel(); return cancellationRequested; }))
            {
                exportProgressDialog = dialog;
                try { dialog.ShowDialog(this); }
                finally { exportProgressDialog = null; }
            }
        }

        private void CloseExportProgress()
        {
            if (exportProgressDialog != null) exportProgressDialog.Finish();
        }

        private void BuildWorkflowActions(FlowLayoutPanel actions)
        {
            undoButton.Text = UiText.T("撤销", "Undo"); undoButton.AutoSize = true; undoButton.Enabled = false;
            undoButton.Click += delegate { UndoEdit(); }; actions.Controls.Add(undoButton);
            actions.Controls.Add(MakeMenuButton("更多 ▾", delegate(ContextMenuStrip menu)
            {
                menu.Items.Add(UiText.T("命名预览", "Batch names"), null, PreviewBatchNames);
                menu.Items.Add(UiText.T("审查疑似重复", "Review duplicates"), null, ReviewDuplicates);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(UiText.T("重试预览", "Retry previews"), null, delegate { if (!worker.IsBusy) StartScan(); });
                menu.Items.Add(UiText.T("仅重试失败项", "Retry failures"), null, RetryFailed).Enabled = retryFailedButton.Enabled;
            }));
            retryFailedButton.Text = UiText.T("仅重试失败项", "Retry failures"); retryFailedButton.AutoSize = true;
            retryFailedButton.Enabled = false; retryFailedButton.Click += RetryFailed; actions.Controls.Add(retryFailedButton);
            retryFailedButton.Visible = false;
            workerWatchdog.Interval = 15000;
            workerWatchdog.Tick += delegate
            {
                if (worker.IsBusy && DateTime.UtcNow - lastWorkerProgressUtc > TimeSpan.FromSeconds(120))
                    progressLabel.Text = cancellationRequested ? UiText.T("已请求取消，仍在等待 SolidWorks 同步调用返回。不会强制关闭用户会话。", "Cancellation requested; still waiting for the SolidWorks call to return. The user session will remain open.") : UiText.T("SolidWorks 已超过 2 分钟没有返回进度；可能正在同步计算。可以请求取消，程序将在安全边界处理。", "SolidWorks has not reported progress for 2 minutes and may be computing. Cancellation is handled at the next safe boundary.");
            };
            workerWatchdog.Start();
            FormClosed += delegate { workerWatchdog.Dispose(); DisposeThumbnailCache(); };
        }

        private Control BuildBodyFilters()
        {
            TableLayoutPanel row = new TableLayoutPanel { Height = 42, ColumnCount = 4, RowCount = 1, Padding = new Padding(2), Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            row.Controls.Add(new Label { Text = UiText.T("搜索", "Search"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            bodySearch.Dock = DockStyle.Fill;
            bodySearch.TextChanged += delegate { if (!gridRefreshing && !worker.IsBusy) { CommitGrid(); RefreshGrid(); } };
            row.Controls.Add(bodySearch, 1, 0);
            bodyFilter.DropDownStyle = ComboBoxStyle.DropDownList; bodyFilter.Dock = DockStyle.Fill;
            bodyFilter.Items.AddRange(new object[] { UiText.T("全部状态", "All statuses"), UiText.T("未分类", "Unclassified"), UiText.T("失败", "Failed"), UiText.T("疑似重复", "Possible duplicates") });
            bodyFilter.SelectedIndex = 0;
            bodyFilter.SelectedIndexChanged += delegate { if (!gridRefreshing && !worker.IsBusy) { CommitGrid(); RefreshGrid(); } };
            row.Controls.Add(bodyFilter, 2, 0);
            compactList.Text = UiText.T("紧凑列表", "Compact list"); compactList.AutoSize = true;
            compactList.CheckedChanged += delegate { CommitGrid(); ApplyCompactMode(); };
            compactList.Anchor = AnchorStyles.Left;
            row.Controls.Add(compactList, 3, 0);
            return row;
        }

        private IEnumerable<BodyRecord> FilterBodies(IEnumerable<BodyRecord> items)
        {
            string query = bodySearch.Text.Trim();
            if (query.Length > 0) items = items.Where(body => (body.OriginalName ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || (body.ExportName ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
            if (bodyFilter.SelectedIndex == 1) items = items.Where(body => body.CategoryId == CategoryNode.UnclassifiedId);
            if (bodyFilter.SelectedIndex == 2) items = items.Where(body => (body.Status ?? "").Contains("失败") || (body.Status ?? "").IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0);
            if (bodyFilter.SelectedIndex == 3) items = items.Where(IsDuplicateCandidate);
            return items;
        }

        private bool IsDuplicateCandidate(BodyRecord body)
        {
            return candidateBodyIds.Contains(body.Id);
        }

        private void IndexDuplicateCandidates()
        {
            candidateBodyIds = new HashSet<string>(project.AllBodies().Where(body => !body.CandidateSuppressed && string.IsNullOrWhiteSpace(body.ConfirmedDuplicateGroupId) && !string.IsNullOrWhiteSpace(body.GeometryKey)).GroupBy(body => body.GeometryKey).Where(group => group.Count() > 1).SelectMany(group => group).Select(body => body.Id));
        }

        private void ApplyCompactMode()
        {
            if (bodyGrid.Columns.Count == 0) return;
            foreach (string name in new[] { "ThumbnailIso", "ThumbnailFront", "ThumbnailTop" }) bodyGrid.Columns[name].Visible = !compactList.Checked;
            bodyGrid.Columns["OriginalName"].Visible = !compactList.Checked;
            int height = compactList.Checked ? Math.Max(30, (int)(34 * project.ListZoomPercent / 100F)) : Math.Max(62, (int)(78 * project.ListZoomPercent / 100F));
            bodyGrid.RowTemplate.Height = height;
            foreach (DataGridViewRow row in bodyGrid.Rows) row.Height = height;
        }

        private void SyncGuidedModelToMain()
        {
            bool previous = suppressDirty;
            suppressDirty = true;
            outputBox.Text = project.OutputRoot;
            suppressDirty = previous;
            RefreshGrid();
        }

        private void ApplyScanResponse(WorkerResponse response)
        {
            // Preserve the V1.2.5 interaction contract: rescanning keeps a body's
            // edited name/category/selection when its index and geometry fingerprint
            // still agree. Do this in the UI process in O(n), not through O(n^2) COM calls.
            // Failed or unvisited files never replace the user's previous model.
            foreach (SourceRecord scanned in response.Sources ?? new List<SourceRecord>())
            {
                int index = project.Sources.FindIndex(source => source.Id == scanned.Id);
                if (scanned.Status == "读取完成")
                {
                    SourceRecord previous = index < 0 ? null : project.Sources[index];
                    RestoreScanEdits(previous, scanned, project.Categories);
                    if (index < 0) project.Sources.Add(scanned); else project.Sources[index] = scanned;
                }
                else if (index >= 0)
                {
                    SourceRecord previous = project.Sources[index];
                    previous.Status = "读取失败";
                    previous.Message = scanned.Message ?? response.Message;
                    foreach (BodyRecord body in previous.Bodies ?? new List<BodyRecord>()) { body.Status = "读取失败，需重读"; body.Message = previous.Message; }
                }
            }
        }

        internal static void RestoreScanEdits(SourceRecord previous, SourceRecord scanned, IList<CategoryNode> categories)
        {
            if (previous == null || scanned == null) return;
            List<BodyRecord> previousBodies = previous.Bodies ?? new List<BodyRecord>();
            List<BodyRecord> newBodies = scanned.Bodies ?? new List<BodyRecord>();
            HashSet<string> used = new HashSet<string>();
            int restored = 0;
            HashSet<string> categoryIds = new HashSet<string>((categories ?? new List<CategoryNode>()).Select(category => category.Id));
            foreach (BodyRecord body in scanned.Bodies ?? new List<BodyRecord>())
            {
                Func<BodyRecord, bool> compatible = candidate =>
                    string.Equals(candidate.Configuration, body.Configuration, StringComparison.Ordinal) &&
                    ((!string.IsNullOrWhiteSpace(candidate.GeometryEvidenceKey) && candidate.GeometryEvidenceKey == body.GeometryEvidenceKey) ||
                     (string.IsNullOrWhiteSpace(candidate.GeometryEvidenceKey) && !string.IsNullOrWhiteSpace(candidate.GeometryKey) && candidate.GeometryKey == body.GeometryKey));
                List<BodyRecord> matches = previousBodies.Where(candidate => compatible(candidate) &&
                    !string.IsNullOrWhiteSpace(body.PersistReference) && candidate.PersistReference == body.PersistReference).ToList();
                bool uniqueNew = newBodies.Count(candidate => candidate.Configuration == body.Configuration && candidate.PersistReference == body.PersistReference) == 1;
                if (matches.Count != 1 || !uniqueNew)
                {
                    matches = previousBodies.Where(candidate => compatible(candidate) && !string.IsNullOrWhiteSpace(body.OriginalName) && candidate.OriginalName == body.OriginalName).ToList();
                    uniqueNew = newBodies.Count(candidate => candidate.Configuration == body.Configuration && candidate.OriginalName == body.OriginalName) == 1;
                }
                // Legacy records without names/references retain the old index+fingerprint path.
                if (matches.Count == 0 && string.IsNullOrWhiteSpace(body.OriginalName) && string.IsNullOrWhiteSpace(body.PersistReference))
                {
                    matches = previousBodies.Where(candidate => candidate.Index == body.Index && compatible(candidate) && string.IsNullOrWhiteSpace(candidate.OriginalName) && string.IsNullOrWhiteSpace(candidate.PersistReference)).ToList();
                    uniqueNew = newBodies.Count(candidate => candidate.Index == body.Index) == 1;
                }
                if (matches.Count != 1 || !uniqueNew || !used.Add(matches[0].Id)) continue;
                BodyRecord old = matches[0];
                restored++;
                body.Id = old.Id;
                body.ExportName = old.ExportName;
                body.CategoryId = categoryIds.Contains(old.CategoryId)
                    ? old.CategoryId : CategoryNode.UnclassifiedId;
                body.ExportSelected = old.ExportSelected;
                if (string.Equals(previous.ContentSha256, scanned.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    body.ConfirmedDuplicateGroupId = old.ConfirmedDuplicateGroupId;
                    body.CandidateSuppressed = old.CandidateSuppressed;
                }
            }
            if (previousBodies.Count > 0)
                scanned.Message = (scanned.Message ?? string.Empty) + string.Format(UiText.T(" 已恢复 {0}/{1} 个实体的命名、分类和勾选；未匹配实体请检查。", " Restored names, categories and selection for {0}/{1} bodies; review unmatched bodies."), restored, newBodies.Count);
        }

        private void ExportOptionsChanged(object sender, EventArgs e)
        {
            if (suppressDirty || changingOptions) return;
            changingOptions = true;
            try
            {
                if (ReferenceEquals(sender, stepOnlyCheck))
                {
                    if (stepOnlyCheck.Checked) { stepCheck.Checked = true; sldprtCheck.Checked = false; assemblyCheck.Checked = false; }
                    else sldprtCheck.Checked = true;
                }
                else if ((ReferenceEquals(sender, sldprtCheck) && sldprtCheck.Checked) ||
                    (ReferenceEquals(sender, stepCheck) && !stepCheck.Checked) || (ReferenceEquals(sender, assemblyCheck) && assemblyCheck.Checked)) stepOnlyCheck.Checked = false;
                if (assemblyCheck.Checked && dedupCheck.Checked)
                {
                    if (ReferenceEquals(sender, dedupCheck)) assemblyCheck.Checked = false; else dedupCheck.Checked = false;
                    if (!Program.SuppressStartupPrompts) MessageBox.Show(this, UiText.T("原位装配体需要每个实例的原始位置，因此不能同时启用重复组只导出一件。已关闭另一个选项。", "In-place assemblies require every instance position, so they cannot be combined with exporting one item per duplicate group. The other option was turned off."), UiText.T("选项说明", "Export options"));
                }
                bool dedupChanged = project.Export.Deduplicate != dedupCheck.Checked;
                if (dedupChanged) CommitGrid();
                if (!stepOnlyCheck.Checked && (stepCheck.Checked || assemblyCheck.Checked)) sldprtCheck.Checked = true;
                project.Export.StepOnly = stepOnlyCheck.Checked;
                // The existing STEP backend still builds verified intermediate parts.
                project.Export.ExportSldprt = sldprtCheck.Checked || stepOnlyCheck.Checked;
                project.Export.ExportStep = stepCheck.Checked;
                project.Export.CreateExcel = reportCheck.Checked;
                project.Export.CreateAssembly = assemblyCheck.Checked;
                project.Export.Deduplicate = dedupCheck.Checked;
                project.Export.ConflictPolicy = conflictCombo.SelectedIndex == 1 ? "自动编号" : conflictCombo.SelectedIndex == 2 ? "覆盖" : "跳过";
                UpdateStepFolderHint();
                MarkProjectDirty();
                if (dedupChanged) RefreshGrid();
                else UpdateSelectionSummary();
            }
            finally { changingOptions = false; }
        }

        private void RememberUndo()
        {
            if (suppressDirty || worker.IsBusy) return;
            undoHistory.Push(new EditUndoState(project));
            if (undoHistory.Count > 30)
            {
                EditUndoState[] latest = undoHistory.Take(30).Reverse().ToArray();
                undoHistory.Clear(); foreach (EditUndoState state in latest) undoHistory.Push(state);
            }
            undoButton.Enabled = true;
        }

        private void UndoEdit()
        {
            if (worker.IsBusy) return;
            if (exportNameEditor.Visible) { CancelExportNameEdit(); return; }
            if (undoHistory.Count == 0) return;
            undoHistory.Pop().Restore(project);
            undoButton.Enabled = undoHistory.Count > 0;
            folderCanvas.Nodes = project.Categories;
            MarkProjectDirty(); RefreshGrid();
        }

        private void PreviewBatchNames(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            CommitGrid();
            List<BodyRecord> bodies = bodyGrid.SelectedRows.Cast<DataGridViewRow>().OrderBy(row => row.Index).Select(row => row.Tag as BodyRecord).Where(body => body != null).ToList();
            if (bodies.Count == 0) return;
            using (TextPrompt prompt = new TextPrompt(UiText.T("批量命名预览", "Batch naming preview"), UiText.T("输入前缀，自动追加 _001、_002…", "Prefix; _001, _002… will be appended"), "Part"))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK) return;
                Dictionary<string, string> names = new Dictionary<string, string>();
                for (int i = 0; i < bodies.Count; i++) foreach (BodyRecord member in GetGroupMembers(bodies[i])) names[member.Id] = NameRules.SafeStem(prompt.Value, "Part") + "_" + (i + 1).ToString("D3");
                var conflict = project.AllBodies().GroupBy(body => project.Export.Deduplicate ? GeometryGroupKey(body) : "id:" + body.Id).Select(group => group.First()).GroupBy(body =>
                    ((project.Export.ExportStep || project.Export.CreateAssembly) ? "" : DisplayCategoryPath(body.CategoryId) + "|") + (names.ContainsKey(body.Id) ? names[body.Id] : body.ExportName), StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
                if (conflict != null) { MessageBox.Show(this, UiText.T("预览发现重名，未提交：", "Name collision; nothing committed: ") + conflict.Key); return; }
                string preview = string.Join("\n", bodies.Select(body => body.ExportName + " → " + names[body.Id]).ToArray());
                if (!ReviewNames(preview)) return;
                RememberUndo(); foreach (BodyRecord body in project.AllBodies()) if (names.ContainsKey(body.Id)) body.ExportName = names[body.Id];
                MarkProjectDirty(); RefreshGrid();
            }
        }

        private bool ReviewNames(string text)
        {
            using (Form review = new Form { Text = UiText.T("确认批量名称", "Confirm batch names"), Size = new Size(720, 500), StartPosition = FormStartPosition.CenterParent })
            {
                TextBox preview = new TextBox { Text = text, Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false };
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
                Button apply = new Button { Text = UiText.T("统一提交", "Apply all"), AutoSize = true, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = UiText.T("取消", "Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
                buttons.Controls.Add(apply); buttons.Controls.Add(cancel); review.Controls.Add(preview); review.Controls.Add(buttons); review.CancelButton = cancel;
                return review.ShowDialog(this) == DialogResult.OK;
            }
        }

        private void ReviewDuplicates(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            CommitGrid();
            using (DuplicateReviewForm dialog = new DuplicateReviewForm(project, RememberUndo, delegate { MarkProjectDirty(); RefreshGrid(); })) dialog.ShowDialog(this);
        }

        private void RetryFailed(object sender, EventArgs e)
        {
            if (worker.IsBusy || lastExportRequest == null || lastExportResponse == null) return;
            retryBodyIds = new HashSet<string>((lastExportResponse.ExportResults ?? new List<ExportResultItem>()).Where(IsFailedResult).Select(item => item.BodyId));
            if (retryBodyIds.Count == 0) { retryBodyIds = null; return; }
            StartExport();
            retryBodyIds = null;
        }

        private static bool IsFailedResult(ExportResultItem item)
        {
            return item.Outcome == "失败" || item.SldprtStatus == "失败" || item.StepStatus == "失败" || item.AssemblyStatus == "失败" || item.AssemblyStepStatus == "失败";
        }

        private static WorkerResponse RecoverWorkerResult(string requestPath, string responsePath, string processFailure)
        {
            WorkerRequest request = JsonFile.Load<WorkerRequest>(requestPath);
            WorkerResponse response = null;
            bool finalRead = false;
            try { if (File.Exists(responsePath)) { response = JsonFile.Load<WorkerResponse>(responsePath); if (response.TaskId != request.TaskId) throw new InvalidDataException("Task identity mismatch / 任务身份不匹配"); finalRead = true; } } catch (Exception ex) { response = null; processFailure += "\n" + ex.Message; }
            if (response == null && !string.IsNullOrWhiteSpace(request.CheckpointPath))
                try { if (File.Exists(request.CheckpointPath)) { response = JsonFile.Load<WorkerResponse>(request.CheckpointPath); if (response.TaskId != request.TaskId) response = null; } } catch { response = null; }
            if (response == null) response = new WorkerResponse();
            if (!finalRead)
            {
                response.Success = false;
                response.TaskId = request.TaskId;
                response.Message = UiText.T("工作进程没有返回最终结果。已恢复可用检查点；未执行项不会计为成功。", "The worker did not return a final response. Available checkpoints were recovered; unexecuted items are not successes.") + "\n" + processFailure;
                response.SolidWorksKeptOpen = true; // ownership is uncertain; protect the session
                foreach (ExportResultItem item in response.ExportResults)
                {
                    if (item.SldprtStatus == "已生成") item.SldprtStatus = "失败";
                    if (item.StepStatus == "待批量导出") item.StepStatus = "失败";
                    if (item.AssemblyStatus == "处理中") item.AssemblyStatus = "失败";
                    if (item.AssemblyStepStatus == "待批量导出") item.AssemblyStepStatus = "失败";
                    WorkerMain.UpdateOutcome(item);
                    if (item.Outcome != "本次成功") item.Message += "\n" + response.Message;
                }
                foreach (ExportPlanItem plan in request.ExportItems ?? new List<ExportPlanItem>())
                    if (!response.ExportResults.Any(item => item.BodyId == plan.BodyId)) response.ExportResults.Add(new ExportResultItem { BodyId = plan.BodyId, SourcePath = plan.SourcePath, SourceName = plan.SourceName, OriginalName = plan.OriginalName, ExportName = plan.ExportName, PlannedExportName = plan.ExportName, CategoryPath = plan.CategoryPath, Quantity = plan.Quantity, Occurrences = plan.Occurrences, PreviewIso = plan.PreviewIso, PreviewFront = plan.PreviewFront, PreviewTop = plan.PreviewTop, Outcome = "未执行", VerificationStatus = "未执行", Message = response.Message });
            }
            // A crashed worker may leave a live macro using its temporary parts.
            // Exclude those prerequisites from delivery counts without deleting them.
            if (!finalRead) WorkerMain.FinishStepOnly(request, response, false);
            return response;
        }

        private Image GetThumbnail(string path)
        {
            string key = string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? "missing" : path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            Image cached;
            if (!thumbnailCache.TryGetValue(key, out cached))
            {
                if (thumbnailCache.Count >= 600) return LoadThumbnail(path);
                cached = LoadThumbnail(path);
                thumbnailCache[key] = cached;
            }
            return cached;
        }

        private static Image LoadThumbnail(string path)
        {
            using (Image source = LoadImage(path))
            {
                Bitmap image = new Bitmap(140, 100);
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.FromArgb(246, 247, 249));
                    if (source == null)
                    {
                        using (Font font = new Font("Microsoft YaHei UI", 8F))
                            graphics.DrawString(UiText.T("预览缺失\n点击“重试预览”", "Preview missing\nUse Retry previews"), font, Brushes.DimGray, new RectangleF(6, 18, 128, 78));
                    }
                    else
                    {
                        double scale = Math.Min(140.0 / source.Width, 100.0 / source.Height);
                        int width = (int)(source.Width * scale), height = (int)(source.Height * scale);
                        graphics.DrawImage(source, (140 - width) / 2, (100 - height) / 2, width, height);
                    }
                }
                return image;
            }
        }

        private void DisposeThumbnailCache()
        {
            foreach (Image image in thumbnailCache.Values.Distinct()) if (image != null) image.Dispose();
            thumbnailCache.Clear();
        }
    }

    internal sealed class ReviewIssue
    {
        internal string BodyId, SourceId, Column, Message;
    }

    internal sealed class ExportProgressDialog : Form
    {
        private readonly ProgressBar bar = new ProgressBar();
        private readonly Label percent = new Label(), stage = new Label();
        private readonly Button cancel = new Button();
        private bool finished, cancellationSent;
        internal ExportProgressDialog(Func<bool> requestCancel)
        {
            Text = UiText.T("导出进行中", "Export in progress"); UiBrand.ApplyIcon(this);
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false; ShowInTaskbar = false; ClientSize = new Size(560, 290);
            BackColor = Color.White; Font = UiBrand.CreateFont(UiBrand.BaseFontSize);
            Controls.Add(new Label { Text = UiText.T("喵师傅正在施工", "Master Miao is at work"), Left = 24, Top = 22, Width = 512, Height = 35,
                Font = UiBrand.CreateFont(17, FontStyle.Bold), ForeColor = Color.FromArgb(215, 25, 32), TextAlign = ContentAlignment.MiddleCenter });
            Controls.Add(new Label { Text = UiText.T("请勿操作 SolidWorks 或 Master Miao，直到导出结束。", "Please do not operate SolidWorks or Master Miao until export finishes."), Left = 24, Top = 71, Width = 512, Height = 46, TextAlign = ContentAlignment.MiddleCenter });
            percent.SetBounds(24, 116, 512, 32); percent.TextAlign = ContentAlignment.MiddleCenter; percent.Font = UiBrand.CreateFont(14, FontStyle.Bold);
            bar.Style = ProgressBarStyle.Continuous;
            bar.SetBounds(24, 156, 512, 22); stage.SetBounds(24, 185, 512, 45); stage.TextAlign = ContentAlignment.MiddleCenter;
            cancel.Text = UiText.T("取消任务", "Cancel task"); cancel.SetBounds(210, 240, 140, 32);
            cancel.Click += delegate
            {
                if (cancellationSent) return;
                if (!requestCancel()) return;
                cancellationSent = true; cancel.Enabled = false;
                stage.Text = UiText.T("已请求取消，等待安全结束。请勿操作 SolidWorks。", "Cancellation requested; waiting for a safe stop. Do not operate SolidWorks.");
            };
            Controls.AddRange(new Control[] { percent, bar, stage, cancel });
            UpdateProgress(0, UiText.T("正在准备…", "Preparing…"));
        }
        internal void UpdateProgress(int value, string message)
        {
            bar.Value = Math.Max(bar.Value, Math.Max(0, Math.Min(99, value)));
            percent.Text = bar.Value + "%";
            if (!cancellationSent) stage.Text = UiText.IsEnglish && (message ?? "").Any(c => c >= 0x4e00 && c <= 0x9fff) ? "Working…" : message;
        }
        internal static int TaskPercent(int value, string stage, bool includesStep)
        {
            // The existing backend reports native-part progress on a separate 0–99 scale.
            return includesStep && (stage == "导出零件" || stage == "生成装配体") ? value * 72 / 100 : value;
        }
        internal void Finish() { finished = true; Close(); }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (!finished) e.Cancel = true; base.OnFormClosing(e); }
    }

    internal sealed class EditUndoState
    {
        private readonly List<CategoryNode> categories;
        private readonly Dictionary<string, BodyRecord> bodies;
        public EditUndoState(AppProject project) { categories = JsonFile.Clone(project.Categories); bodies = project.AllBodies().ToDictionary(body => body.Id, body => new BodyRecord { ExportName = body.ExportName, CategoryId = body.CategoryId, ExportSelected = body.ExportSelected, ConfirmedDuplicateGroupId = body.ConfirmedDuplicateGroupId, CandidateSuppressed = body.CandidateSuppressed }); }
        public void Restore(AppProject project)
        {
            project.Categories = categories;
            foreach (BodyRecord body in project.AllBodies()) { BodyRecord old; if (!bodies.TryGetValue(body.Id, out old)) continue; body.ExportName = old.ExportName; body.CategoryId = old.CategoryId; body.ExportSelected = old.ExportSelected; body.ConfirmedDuplicateGroupId = old.ConfirmedDuplicateGroupId; body.CandidateSuppressed = old.CandidateSuppressed; }
        }
    }

    internal sealed class DuplicateReviewForm : Form
    {
        private readonly AppProject project;
        private readonly Action beforeChange, changed;
        private readonly ComboBox groups = new ComboBox();
        private readonly CheckedListBox members = new CheckedListBox();
        private readonly PictureBox[] views = new PictureBox[3];
        private List<List<BodyRecord>> currentGroups;
        public DuplicateReviewForm(AppProject project, Action beforeChange, Action changed)
        {
            this.project = project; this.beforeChange = beforeChange; this.changed = changed;
            Text = UiText.T("重复组审查与单独保留", "Duplicate review and exclusions"); Size = new Size(1000, 640); MinimumSize = new Size(800, 500); StartPosition = FormStartPosition.CenterParent;
            UiBrand.ApplyIcon(this);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = UiText.T("勾选去重后列表自动折叠，取消勾选可恢复全部实体；导出前会验证组内实体几何。可排除需要单独生产的成员。当前不比较材料、表面处理等生产属性，请自行核对。", "Deduplication folds matching rows immediately; disabling it restores all bodies. Solids are verified before export. Exclude members that must be produced separately. Materials and finishes are not compared; review those yourself.") }, 0, 0);
            groups.Dock = DockStyle.Fill; groups.DropDownStyle = ComboBoxStyle.DropDownList; groups.SelectedIndexChanged += delegate { LoadMembers(); }; root.Controls.Add(groups, 0, 1);
            members.Dock = DockStyle.Fill; members.CheckOnClick = true; members.SelectedIndexChanged += delegate { ShowViews(); }; root.Controls.Add(members, 0, 2);
            TableLayoutPanel images = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            for (int i = 0; i < 3; i++) { images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F)); views[i] = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom }; images.Controls.Add(views[i], i, 0); }
            root.Controls.Add(images, 0, 3);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
            AddButton(buttons, UiText.T("确认合并勾选成员", "Confirm selected members"), Confirm);
            AddButton(buttons, UiText.T("排除勾选成员", "Exclude selected"), Exclude);
            AddButton(buttons, UiText.T("取消本组分组", "Ungroup"), Ungroup);
            root.Controls.Add(buttons, 0, 4); Controls.Add(root);
            FormClosed += delegate { foreach (PictureBox box in views) if (box.Image != null) box.Image.Dispose(); };
            Reload();
        }
        private void AddButton(Control parent, string text, EventHandler handler) { Button b = new Button { Text = text, AutoSize = true }; b.Click += handler; parent.Controls.Add(b); }
        private void Reload()
        {
            currentGroups = project.AllBodies().Where(body => !body.CandidateSuppressed && (!string.IsNullOrWhiteSpace(body.GeometryKey) || !string.IsNullOrWhiteSpace(body.ConfirmedDuplicateGroupId))).GroupBy(body => string.IsNullOrWhiteSpace(body.ConfirmedDuplicateGroupId) ? "candidate:" + body.GeometryKey : "confirmed:" + body.ConfirmedDuplicateGroupId).Where(group => group.Count() > 1).Select(group => group.ToList()).ToList();
            groups.Items.Clear(); foreach (List<BodyRecord> group in currentGroups) groups.Items.Add((string.IsNullOrWhiteSpace(group[0].ConfirmedDuplicateGroupId) ? UiText.T("疑似", "Possible") : UiText.T("已确认", "Confirmed")) + " · " + group.Count + " · " + group[0].ExportName);
            if (groups.Items.Count > 0) groups.SelectedIndex = 0; else members.Items.Clear();
        }
        private void LoadMembers() { members.Items.Clear(); if (groups.SelectedIndex < 0) return; foreach (BodyRecord body in currentGroups[groups.SelectedIndex]) members.Items.Add(body.SourceName + " / " + body.OriginalName + " → " + body.ExportName, true); if (members.Items.Count > 0) members.SelectedIndex = 0; }
        private List<BodyRecord> Checked() { if (groups.SelectedIndex < 0) return new List<BodyRecord>(); return members.CheckedIndices.Cast<int>().Select(i => currentGroups[groups.SelectedIndex][i]).ToList(); }
        private void Confirm(object sender, EventArgs e)
        {
            List<BodyRecord> selected = Checked(); if (selected.Count < 2) return;
            if (MessageBox.Show(this, UiText.T("确认这些成员可作为同一零件生产？将采用第一个成员的名称、分类和导出选择。此操作是人工确认，不能代替几何/生产属性验证。", "Confirm these members can be produced as one part? The first member's name, category and selection will be used. This is a manual decision, not proof of geometry or manufacturing equivalence."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            beforeChange(); string id = Guid.NewGuid().ToString("N"); BodyRecord first = selected[0];
            foreach (BodyRecord body in currentGroups[groups.SelectedIndex]) body.ConfirmedDuplicateGroupId = string.Empty;
            foreach (BodyRecord body in selected) { body.ConfirmedDuplicateGroupId = id; body.ExportName = first.ExportName; body.CategoryId = first.CategoryId; body.ExportSelected = first.ExportSelected; }
            changed(); Reload();
        }
        private void Exclude(object sender, EventArgs e) { List<BodyRecord> selected = Checked(); if (selected.Count == 0) return; beforeChange(); foreach (BodyRecord body in selected) { body.CandidateSuppressed = true; body.ConfirmedDuplicateGroupId = string.Empty; } changed(); Reload(); }
        private void Ungroup(object sender, EventArgs e) { if (groups.SelectedIndex < 0) return; beforeChange(); foreach (BodyRecord body in currentGroups[groups.SelectedIndex]) { body.ConfirmedDuplicateGroupId = string.Empty; body.CandidateSuppressed = true; } changed(); Reload(); }
        private void ShowViews()
        {
            if (groups.SelectedIndex < 0 || members.SelectedIndex < 0) return;
            BodyRecord body = currentGroups[groups.SelectedIndex][members.SelectedIndex]; string[] paths = { body.PreviewIso, body.PreviewFront, body.PreviewTop };
            for (int i = 0; i < 3; i++) { if (views[i].Image != null) views[i].Image.Dispose(); views[i].Image = null; if (File.Exists(paths[i])) using (Image source = Image.FromFile(paths[i])) views[i].Image = new Bitmap(source); }
        }
    }
}
