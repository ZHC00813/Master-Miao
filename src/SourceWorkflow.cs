using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SWBodyOrganizer
{
    internal static class SourceDocuments
    {
        internal static IModelDoc2 FindOpen(ISldWorks app, string path)
        {
            // GetOpenDocumentByName may also resolve a title. Never accept a different path.
            IModelDoc2 found = app.GetOpenDocumentByName(path) as IModelDoc2;
            if (found != null && string.Equals(found.GetPathName(), path, StringComparison.OrdinalIgnoreCase)) return found;
            ExportIntegrity.Release(found);
            object[] documents = app.GetDocuments() as object[] ?? new object[0];
            string conflict = null;
            IModelDoc2 exact = null;
            foreach (object value in documents)
            {
                IModelDoc2 model = value as IModelDoc2;
                string opened = model == null ? string.Empty : model.GetPathName();
                if (string.Equals(opened, path, StringComparison.OrdinalIgnoreCase)) { exact = model; continue; }
                if (string.Equals(Path.GetFileName(opened), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) conflict = opened;
                ExportIntegrity.Release(value);
            }
            if (exact != null) return exact;
            if (conflict != null) throw new InvalidDataException("SolidWorks 已打开另一个同名文件。\n当前项目：" + path + "\n已打开：" + conflict + "\n若已另存，请使用“重新关联”选择保存后的文件，命名和分类将按实体身份恢复；否则先保存并关闭冲突文档。 / Another document with the same filename is open. Relink the saved source or close the conflicting document.");
            return null;
        }

        internal static string OpenError(string path, int errors, int warnings)
        {
            return "无法打开源文件：" + path + "；错误=" + errors + "，警告=" + warnings +
                ((errors & 65536) != 0 ? "。存在同名文档，请保存并关闭冲突文件，或使用“重新关联”（保留命名）。" : "。请检查文件及 SolidWorks 状态后重新读取（保留命名）。");
        }

        internal static void SaveSources(ISldWorks app, IEnumerable<SourceRecord> sources)
        {
            foreach (SourceRecord source in sources)
            {
                IModelDoc2 model = FindOpen(app, source.Path);
                try
                {
                    int errors = 0, warnings = 0;
                    if (model == null)
                        model = app.OpenDoc6(source.Path, 1, 1, string.Empty, ref errors, ref warnings);
                    if (model == null) throw new IOException(OpenError(source.Path, errors, warnings));
                    // Materializing the bodies can trigger a deferred rebuild and
                    // set the save flag. Do this before the explicitly requested save.
                    object[] bodies = ((SolidWorks.Interop.sldworks.IPartDoc)model).GetBodies2(0, false) as object[];
                    if (bodies != null) foreach (object body in bodies) ExportIntegrity.Release(body);
                    if (!model.GetSaveFlag()) continue;
                    EnsureWritable(model);
                    if (!model.Save3(1, ref errors, ref warnings) || errors != 0 || model.GetSaveFlag())
                        throw new IOException("源零件未能保存：" + source.Path + "；错误=" + errors + "，警告=" + warnings + "。请在 SolidWorks 中保存；若另存到新位置，使用“重新关联”，无需删除原记录。 / Could not save the source. Save in SolidWorks, then relink if its path changed.");
                }
                finally { ExportIntegrity.Release(model); }
            }
        }

        internal static void EnsureWritable(IModelDoc2 model)
        {
            if (model.IsOpenedReadOnly() && (!model.SetReadOnlyState(false) || model.IsOpenedReadOnly()))
                throw new IOException("源零件被其他会话占用或文件为只读，无法保存。请先释放占用，或另存后使用“重新关联”保留命名。 / Source is locked or read-only; release the lock, or Save As and relink.");
        }
    }

    public partial class MainForm
    {
        private bool BackupBeforeSourceChange()
        {
            if (!project.AllBodies().Any()) return true;
            try
            {
                string folder = Path.Combine(AppPaths.Backups, "BeforeRescan");
                Directory.CreateDirectory(folder);
                ProjectStore.Save(Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".swbody.json"), project);
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, "重读前的命名备份失败，已停止操作：\n" + ex.Message); return false; }
        }

        private void RelinkSelectedSource(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            CommitGrid();
            SourceListItem selected = sourceList.SelectedItem as SourceListItem;
            SourceRecord source = selected == null ? null : project.Sources.FirstOrDefault(item => item.Id == selected.Id);
            if (source == null && project.Sources.Count == 1) source = project.Sources[0];
            if (source == null) { MessageBox.Show(this, UiText.T("请先选择要重新关联的源文件。", "Select a source file first.")); return; }
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "SolidWorks 零件 (*.SLDPRT)|*.SLDPRT", Title = UiText.T("选择保存后的源零件（保留命名和分类）", "Relink saved source (preserve names and categories)"), FileName = source.Name })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (project.Sources.Any(item => item.Id != source.Id && string.Equals(item.Path, dialog.FileName, StringComparison.OrdinalIgnoreCase)))
                { MessageBox.Show(this, UiText.T("此路径已属于另一个源文件记录。", "This path already belongs to another source.")); return; }
                try
                {
                    if (!BackupBeforeSourceChange()) return;
                    ProjectStore.PrepareSourceRelink(source, dialog.FileName);
                    MarkProjectDirty(); RefreshSources(); RefreshGrid(); StartScan();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            }
        }

        private void SaveSourcesAndRescan(object sender, EventArgs e)
        {
            if (worker.IsBusy || project.Sources.Count == 0) return;
            if (MessageBox.Show(this, UiText.T("将保存当前项目源零件在 SolidWorks 中的修改并重新读取。命名、分类及勾选状态将按实体身份恢复。\n\n若文件已另存到新位置，请先使用“重新关联”。是否继续？", "Save changes to the project's open source parts and rescan, preserving edits by body identity? Relink first if saved to a new location."), "Master Miao", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            StartScan(true);
        }
    }
}
