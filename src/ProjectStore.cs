using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SWBodyOrganizer
{
    // The UI owns committed data. Persistence serializes a copy and never reads controls.
    public static class ProjectStore
    {
        private static readonly string instanceId = Guid.NewGuid().ToString("N");
        private static readonly object gate = new object();
        private static readonly Dictionary<string, string> savedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> previewHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string[]> adoptedRecoveries = new Dictionary<string, string[]>();
        private static FileStream recoveryLease;
        public static string LastLoadWarning { get; private set; }

        public static AppProject Load(string path)
        {
            lock (gate) return LoadCore(path);
        }

        private static AppProject LoadCore(string path)
        {
            path = Path.GetFullPath(path);
            bool recovered;
            AppProject project = JsonFile.Load<AppProject>(path, out recovered);
            Normalize(project);
            string directory = Path.GetDirectoryName(path);
            MapPreviews(project, value => ResolvePreview(directory, value));
            savedHashes[path] = File.Exists(path) ? ContentHash(path) : string.Empty;
            LastLoadWarning = recovered ? "主项目文件不可读，已恢复上一份有效备份，请检查后保存 / The primary project could not be read. The last valid backup was recovered; review and save it." : string.Empty;
            return project;
        }

        public static void Save(string path, AppProject project)
        {
            lock (gate) SaveCore(path, project);
        }

        private static void SaveCore(string path, AppProject project)
        {
            path = Path.GetFullPath(path);
            Normalize(project);
            AppProject snapshot = JsonFile.Clone(project);
            snapshot.LastSavedUtc = DateTime.UtcNow;
            string folder = Path.GetDirectoryName(path);
            MapPreviews(snapshot, value => StorePreview(folder, value));
            string expected;
            savedHashes.TryGetValue(path, out expected);
            JsonFile.SaveChecked(path, snapshot, expected);
            savedHashes[path] = ContentHash(path);
            project.LastSavedUtc = snapshot.LastSavedUtc;
        }

        private static void MapPreviews(AppProject project, Func<string, string> map)
        {
            foreach (BodyRecord body in project.AllBodies())
            { body.PreviewIso = map(body.PreviewIso); body.PreviewFront = map(body.PreviewFront); body.PreviewTop = map(body.PreviewTop); }
            if (project.LastTaskRequest != null)
                foreach (ExportPlanItem item in project.LastTaskRequest.ExportItems ?? new List<ExportPlanItem>())
                { item.PreviewIso = map(item.PreviewIso); item.PreviewFront = map(item.PreviewFront); item.PreviewTop = map(item.PreviewTop); }
            if (project.LastTaskResponse != null)
                foreach (ExportResultItem item in project.LastTaskResponse.ExportResults ?? new List<ExportResultItem>())
                { item.PreviewIso = map(item.PreviewIso); item.PreviewFront = map(item.PreviewFront); item.PreviewTop = map(item.PreviewTop); }
        }

        public static void Normalize(AppProject project)
        {
            if (project == null) throw new InvalidDataException("项目为空 / Project is empty.");
            if (project.SchemaVersion > 3) throw new InvalidDataException("项目来自更新的程序版本 / This project requires a newer application.");
            if (string.IsNullOrWhiteSpace(project.ProjectId)) project.ProjectId = Guid.NewGuid().ToString("N");
            project.SchemaVersion = 3;
            if (project.Sources == null) project.Sources = new List<SourceRecord>();
            if (project.Categories == null || project.Categories.Count == 0) project.Categories = CategoryNode.CreateDefaultTree();
            if (project.Export == null) project.Export = new ExportSettings();
            if (project.ListZoomPercent < 80 || project.ListZoomPercent > 200) project.ListZoomPercent = 100;
            foreach (SourceRecord source in project.Sources)
            {
                if (source.Bodies == null) source.Bodies = new List<BodyRecord>();
                source.ContentSha256 = source.ContentSha256 ?? string.Empty;
                foreach (BodyRecord body in source.Bodies)
                {
                    if (string.IsNullOrWhiteSpace(body.Id)) body.Id = Guid.NewGuid().ToString("N");
                    body.SourceId = source.Id;
                    body.SourcePath = source.Path;
                    body.SourceName = source.Name;
                    body.SourceSha256 = body.SourceSha256 ?? string.Empty;
                    body.PersistReference = body.PersistReference ?? string.Empty;
                    body.Configuration = body.Configuration ?? string.Empty;
                    body.GeometryEvidenceKey = body.GeometryEvidenceKey ?? string.Empty;
                    body.GeometryBounds = body.GeometryBounds ?? new double[0];
                    body.ConfirmedDuplicateGroupId = body.ConfirmedDuplicateGroupId ?? string.Empty;
                }
            }
        }

        public static string ResolvePreview(string folder, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(normalized))
            {
                string relative = Path.GetFullPath(Path.Combine(folder, normalized));
                if (!IsInside(folder, relative)) return string.Empty;
                return relative;
            }
            // Prefer the project's own assets after moving a Schema 2 project folder.
            string marker = Path.DirectorySeparatorChar + "Previews" + Path.DirectorySeparatorChar;
            int at = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                string relocated = Path.GetFullPath(Path.Combine(folder, normalized.Substring(at + 1)));
                if (IsInside(folder, relocated) && File.Exists(relocated)) return relocated;
            }
            return normalized;
        }

        private static string StorePreview(string folder, string sourcePath)
        {
            string source = ResolvePreview(folder, sourcePath);
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                return Path.IsPathRooted(source ?? string.Empty) ? string.Empty : source ?? string.Empty;
            FileInfo info = new FileInfo(source);
            string stamp = Path.GetFullPath(source) + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            string hash;
            if (!previewHashes.TryGetValue(stamp, out hash)) { hash = ContentHash(source); previewHashes[stamp] = hash; }
            // Content-addressed assets remain immutable so a failed/stale save cannot
            // change previews referenced by the last committed project or its backup.
            string relativeTarget = Path.Combine("Previews", hash.Substring(0, 2), hash + ".png");
            string target = Path.GetFullPath(Path.Combine(folder, relativeTarget));
            if (string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase)) return relativeTarget;
            if (File.Exists(target)) return relativeTarget;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (!File.Exists(target))
            {
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(source, temp, false);
                    if (!string.Equals(ContentHash(temp), hash, StringComparison.Ordinal)) throw new IOException("预览在保存过程中变化，请重试 / Preview changed while saving; retry.");
                    try { File.Move(temp, target); }
                    catch (IOException) { if (!File.Exists(target) || ContentHash(target) != hash) throw; }
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return relativeTarget;
        }

        public static string SaveRecovery(AppProject project)
        {
            string folder = RecoveryFolder();
            string path = Path.Combine(folder, NameRules.SafeStem(project.ProjectId, "project") + ".swbody.json");
            Save(path, project);
            return path;
        }

        private static string RecoveryFolder()
        {
            AppPaths.Ensure();
            string folder = Path.Combine(AppPaths.Recovery, instanceId);
            Directory.CreateDirectory(folder);
            if (recoveryLease == null) recoveryLease = new FileStream(Path.Combine(folder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return folder;
        }

        public static IEnumerable<string> GetRecoveryFiles()
        {
            AppPaths.Ensure();
            List<string> results = new List<string>();
            string legacy = Path.Combine(AppPaths.Recovery, "autosave.swbody.json");
            if (File.Exists(legacy)) results.Add(legacy);
            foreach (string folder in Directory.GetDirectories(AppPaths.Recovery))
            {
                if (Path.GetFileName(folder) == instanceId) continue;
                try
                {
                    using (FileStream lease = new FileStream(Path.Combine(folder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        results.AddRange(Directory.GetFiles(folder, "*.swbody.json"));
                }
                catch (IOException) { } // A running instance still owns these records.
                catch (UnauthorizedAccessException) { }
            }
            return results.OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        }

        public static void DeleteRecovery(AppProject project)
        {
            string path = Path.Combine(RecoveryFolder(), NameRules.SafeStem(project.ProjectId, "project") + ".swbody.json");
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            string[] adopted;
            if (!adoptedRecoveries.TryGetValue(project.ProjectId, out adopted)) return;
            // Only retire the exact inactive recovery that the user chose and saved.
            using (FileStream lease = new FileStream(Path.Combine(Path.GetDirectoryName(adopted[0]), "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (File.Exists(adopted[0]) && ContentHash(adopted[0]) == adopted[1])
                {
                    File.Delete(adopted[0]);
                    if (File.Exists(adopted[0] + ".bak")) File.Delete(adopted[0] + ".bak");
                }
            }
            adoptedRecoveries.Remove(project.ProjectId);
        }

        public static void AdoptRecovery(string path, AppProject project)
        {
            path = Path.GetFullPath(path);
            if (!IsInside(AppPaths.Recovery, path)) throw new InvalidDataException("Recovery record is outside the recovery folder.");
            adoptedRecoveries[project.ProjectId] = new string[] { path, ContentHash(path) };
        }

        public static bool RebindSource(SourceRecord source, string newPath)
        {
            newPath = Path.GetFullPath(newPath);
            string hash = ContentHash(newPath);
            bool verified = !string.IsNullOrWhiteSpace(source.ContentSha256);
            if (verified && !string.Equals(hash, source.ContentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("所选文件内容与原源文件不同，不能保留实体对应关系；请将其作为新文件导入 / The selected file differs from the original. Import it as a new source.");
            source.Path = newPath;
            source.Name = Path.GetFileName(newPath);
            FileInfo info = new FileInfo(newPath);
            if (verified) { source.Length = info.Length; source.LastWriteTicks = info.LastWriteTimeUtc.Ticks; }
            else { source.Status = "需要重新读取"; source.Message = "旧项目无内容哈希；请重新读取 / Legacy project has no content hash; rescan required."; }
            foreach (BodyRecord body in source.Bodies) { body.SourcePath = newPath; body.SourceName = source.Name; }
            return verified;
        }

        public static bool IsInside(string directory, string file)
        {
            return Path.GetFullPath(file).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static string ContentHash(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
