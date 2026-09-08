using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SWBodyOrganizer
{
    public sealed class AppProject
    {
        public int SchemaVersion { get; set; }
        public string ProjectId { get; set; }
        public string Name { get; set; }
        public string TemplateName { get; set; }
        public string OutputRoot { get; set; }
        public int GuidedIndex { get; set; }
        public int ListZoomPercent { get; set; }
        public DateTime LastSavedUtc { get; set; }
        public DateTime LastExportUtc { get; set; }
        public bool LastExportSucceeded { get; set; }
        public List<SourceRecord> Sources { get; set; }
        public List<CategoryNode> Categories { get; set; }
        public ExportSettings Export { get; set; }
        public WorkerRequest LastTaskRequest { get; set; }
        public WorkerResponse LastTaskResponse { get; set; }

        public AppProject()
        {
            SchemaVersion = 3;
            ProjectId = Guid.NewGuid().ToString("N");
            Name = "未命名项目";
            TemplateName = "默认模板";
            OutputRoot = string.Empty;
            ListZoomPercent = 100;
            Sources = new List<SourceRecord>();
            Categories = CategoryNode.CreateDefaultTree();
            Export = new ExportSettings();
        }

        public IEnumerable<BodyRecord> AllBodies()
        {
            return Sources.SelectMany(source => source.Bodies ?? new List<BodyRecord>());
        }
    }

    public sealed class SourceRecord
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public long Length { get; set; }
        public long LastWriteTicks { get; set; }
        public string ContentSha256 { get; set; }
        public string Configuration { get; set; }
        public int BodyCount { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public List<BodyRecord> Bodies { get; set; }

        public SourceRecord()
        {
            Id = Guid.NewGuid().ToString("N");
            Path = string.Empty;
            Name = string.Empty;
            Configuration = string.Empty;
            ContentSha256 = string.Empty;
            Status = "待读取";
            Message = string.Empty;
            Bodies = new List<BodyRecord>();
        }
    }

    public sealed class BodyRecord
    {
        public string Id { get; set; }
        public string SourceId { get; set; }
        public string SourcePath { get; set; }
        public string SourceName { get; set; }
        public int Index { get; set; }
        public string SourceSha256 { get; set; }
        public string Configuration { get; set; }
        public string PersistReference { get; set; }
        public string GeometryEvidenceKey { get; set; }
        public double[] GeometryBounds { get; set; }
        public double Volume { get; set; }
        public double SurfaceArea { get; set; }
        public string ConfirmedDuplicateGroupId { get; set; }
        public bool CandidateSuppressed { get; set; }
        public string OriginalName { get; set; }
        public string ExportName { get; set; }
        public string CategoryId { get; set; }
        public bool ExportSelected { get; set; }
        public string PreviewFront { get; set; }
        public string PreviewTop { get; set; }
        public string PreviewIso { get; set; }
        public string GeometryKey { get; set; }
        public string DuplicateGroup { get; set; }
        public string Material { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }

        public BodyRecord()
        {
            Id = Guid.NewGuid().ToString("N");
            SourceId = string.Empty;
            SourcePath = string.Empty;
            SourceName = string.Empty;
            SourceSha256 = string.Empty;
            Configuration = string.Empty;
            PersistReference = string.Empty;
            GeometryEvidenceKey = string.Empty;
            GeometryBounds = new double[0];
            ConfirmedDuplicateGroupId = string.Empty;
            OriginalName = string.Empty;
            ExportName = string.Empty;
            CategoryId = CategoryNode.UnclassifiedId;
            ExportSelected = true;
            PreviewFront = string.Empty;
            PreviewTop = string.Empty;
            PreviewIso = string.Empty;
            GeometryKey = string.Empty;
            DuplicateGroup = string.Empty;
            Material = string.Empty;
            Status = "待读取";
            Message = string.Empty;
        }
    }

    public sealed class CategoryNode
    {
        public const string RootId = "root";
        public const string UnclassifiedId = "unclassified";

        public string Id { get; set; }
        public string ParentId { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
        public string ColorHex { get; set; }
        public string Description { get; set; }
        public bool IsSystem { get; set; }

        public CategoryNode()
        {
            Id = Guid.NewGuid().ToString("N");
            ParentId = RootId;
            Name = "新分类";
            ColorHex = "#D71920";
            Description = string.Empty;
        }

        public static List<CategoryNode> CreateDefaultTree()
        {
            return new List<CategoryNode>
            {
                new CategoryNode { Id = RootId, ParentId = string.Empty, Name = "主输出目录", Order = 0, IsSystem = true, ColorHex = "#B5121B" },
                new CategoryNode { Id = UnclassifiedId, ParentId = RootId, Name = "未分类", Order = 9999, IsSystem = true, ColorHex = "#6B7280" }
            };
        }
    }

    public sealed class FolderTemplate
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int Version { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public List<CategoryNode> Categories { get; set; }

        public FolderTemplate()
        {
            Name = "默认模板";
            Description = "工作文件夹与分类标签模板";
            Version = 1;
            CreatedUtc = DateTime.UtcNow;
            UpdatedUtc = DateTime.UtcNow;
            Categories = CategoryNode.CreateDefaultTree();
        }
    }

    public sealed class ExportSettings
    {
        public bool StepOnly { get; set; }
        public bool ExportSldprt { get; set; }
        public bool ExportStep { get; set; }
        public bool SeparateStepOutput { get; set; }
        public bool CreateExcel { get; set; }
        public bool CreateAssembly { get; set; }
        public bool Deduplicate { get; set; }
        public string ConflictPolicy { get; set; }

        public ExportSettings()
        {
            ExportSldprt = true;
            ExportStep = false;
            SeparateStepOutput = false;
            CreateExcel = true;
            CreateAssembly = false;
            Deduplicate = false;
            ConflictPolicy = "跳过";
        }
    }

    public sealed class WorkerRequest
    {
        public string TaskId { get; set; }
        public DateTime StartedUtc { get; set; }
        public string CheckpointPath { get; set; }
        public string Operation { get; set; }
        public string CacheRoot { get; set; }
        public string CancelFile { get; set; }
        public bool GeneratePreviews { get; set; }
        public bool KeepSourceDocumentsOpen { get; set; }
        public List<SourceRecord> Sources { get; set; }
        public List<ExportPlanItem> ExportItems { get; set; }
        public ExportSettings ExportSettings { get; set; }
        public string OutputRoot { get; set; }
        public string StagingRoot { get; set; }
        public int AuthorizedSolidWorksProcessId { get; set; }
        public long AuthorizedSolidWorksStartTimeUtcTicks { get; set; }

        public WorkerRequest()
        {
            TaskId = Guid.NewGuid().ToString("N");
            CheckpointPath = string.Empty;
            Operation = string.Empty;
            CacheRoot = string.Empty;
            CancelFile = string.Empty;
            GeneratePreviews = true;
            Sources = new List<SourceRecord>();
            ExportItems = new List<ExportPlanItem>();
            ExportSettings = new ExportSettings();
            OutputRoot = string.Empty;
            StagingRoot = string.Empty;
        }
    }

    public sealed class WorkerResponse
    {
        public string TaskId { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime CompletedUtc { get; set; }
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string Message { get; set; }
        public string SolidWorksRevision { get; set; }
        public string TemplatePath { get; set; }
        public string AssemblyTemplatePath { get; set; }
        public bool StepAvailable { get; set; }
        public bool SolidWorksKeptOpen { get; set; }
        public int RetainedSourceDocumentCount { get; set; }
        public string StepDiagnostic { get; set; }
        public List<SourceRecord> Sources { get; set; }
        public List<ExportResultItem> ExportResults { get; set; }
        public List<AssemblyResultItem> AssemblyResults { get; set; }

        public WorkerResponse()
        {
            TaskId = string.Empty;
            Message = string.Empty;
            SolidWorksRevision = string.Empty;
            TemplatePath = string.Empty;
            AssemblyTemplatePath = string.Empty;
            StepDiagnostic = string.Empty;
            Sources = new List<SourceRecord>();
            ExportResults = new List<ExportResultItem>();
            AssemblyResults = new List<AssemblyResultItem>();
        }
    }

    public sealed class ExportPlanItem
    {
        public string BodyId { get; set; }
        public string SourcePath { get; set; }
        public string SourceName { get; set; }
        public int BodyIndex { get; set; }
        public string OriginalName { get; set; }
        public string ExportName { get; set; }
        public string CategoryPath { get; set; }
        public string PreviewFront { get; set; }
        public string PreviewTop { get; set; }
        public string PreviewIso { get; set; }
        public string GeometryKey { get; set; }
        public string SourceSha256 { get; set; }
        public string Configuration { get; set; }
        public string PersistReference { get; set; }
        public string GeometryEvidenceKey { get; set; }
        public double[] GeometryBounds { get; set; }
        public double Volume { get; set; }
        public double SurfaceArea { get; set; }
        public int Quantity { get; set; }
        public List<string> Occurrences { get; set; }
        public List<BodyRecord> DuplicateMembers { get; set; }

        public ExportPlanItem()
        {
            BodyId = string.Empty;
            SourcePath = string.Empty;
            SourceName = string.Empty;
            OriginalName = string.Empty;
            ExportName = string.Empty;
            CategoryPath = string.Empty;
            PreviewFront = string.Empty;
            PreviewTop = string.Empty;
            PreviewIso = string.Empty;
            GeometryKey = string.Empty;
            SourceSha256 = string.Empty;
            Configuration = string.Empty;
            PersistReference = string.Empty;
            GeometryEvidenceKey = string.Empty;
            GeometryBounds = new double[0];
            Quantity = 1;
            Occurrences = new List<string>();
            DuplicateMembers = new List<BodyRecord>();
        }
    }

    public sealed class ExportResultItem
    {
        public string PlannedExportName { get; set; }
        public List<string> Occurrences { get; set; }
        public string Outcome { get; set; }
        public string SldprtVerification { get; set; }
        public string StepVerification { get; set; }
        public string AssemblyStepStatus { get; set; }
        public double ExpectedVolume { get; set; }
        public double ExpectedArea { get; set; }
        public double[] ExpectedBounds { get; set; }
        public string ExpectedGeometryEvidenceKey { get; set; }
        public string BodyId { get; set; }
        public string SourcePath { get; set; }
        public string SourceName { get; set; }
        public string OriginalName { get; set; }
        public string ExportName { get; set; }
        public string CategoryPath { get; set; }
        public string PreviewFront { get; set; }
        public string PreviewTop { get; set; }
        public string PreviewIso { get; set; }
        public int Quantity { get; set; }
        public string SldprtPath { get; set; }
        public string StepPath { get; set; }
        public string SldprtStatus { get; set; }
        public string StepStatus { get; set; }
        public string AssemblyPath { get; set; }
        public string AssemblyStepPath { get; set; }
        public string AssemblyStatus { get; set; }
        public string VerificationStatus { get; set; }
        public string Message { get; set; }

        public ExportResultItem()
        {
            PlannedExportName = string.Empty;
            Occurrences = new List<string>();
            Outcome = "未执行";
            SldprtVerification = "未验证";
            StepVerification = "未验证";
            AssemblyStepStatus = "未启用";
            ExpectedBounds = new double[0];
            ExpectedGeometryEvidenceKey = string.Empty;
            BodyId = string.Empty;
            SourcePath = string.Empty;
            SourceName = string.Empty;
            OriginalName = string.Empty;
            ExportName = string.Empty;
            CategoryPath = string.Empty;
            PreviewFront = string.Empty;
            PreviewTop = string.Empty;
            PreviewIso = string.Empty;
            Quantity = 1;
            SldprtPath = string.Empty;
            StepPath = string.Empty;
            SldprtStatus = "未启用";
            StepStatus = "未启用";
            AssemblyPath = string.Empty;
            AssemblyStepPath = string.Empty;
            AssemblyStatus = "未启用";
            VerificationStatus = "未验证";
            Message = string.Empty;
        }
    }

    public sealed class AssemblyResultItem
    {
        public string SourcePath { get; set; }
        public string SourceName { get; set; }
        public string AssemblyPath { get; set; }
        public string StepSourceAssemblyPath { get; set; }
        public string AssemblyStepPath { get; set; }
        public string StepStatus { get; set; }
        public bool Temporary { get; set; }
        public int ComponentCount { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }

        public AssemblyResultItem()
        {
            SourcePath = string.Empty;
            SourceName = string.Empty;
            AssemblyPath = string.Empty;
            StepSourceAssemblyPath = string.Empty;
            AssemblyStepPath = string.Empty;
            StepStatus = "未启用";
            Status = "未启用";
            Message = string.Empty;
        }
    }

    public sealed class CategoryOption
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public override string ToString() { return Path; }
    }

    public static class AppPaths
    {
        public static readonly string Base = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        private static string data = Path.Combine(Base, "Data");
        public static string Data { get { return data; } private set { data = value; } }
        public static string Templates { get { return Path.Combine(Data, "Templates"); } }
        public static string Cache { get { return Path.Combine(Data, "Cache"); } }
        public static string Jobs { get { return Path.Combine(Data, "Jobs"); } }
        public static string Backups { get { return Path.Combine(Data, "Backups"); } }
        public static string Recovery { get { return Path.Combine(Data, "Recovery"); } }
        public static string Settings { get { return Path.Combine(Data, "settings.json"); } }
        public static string StartupDiagnostic { get; private set; }
        private static bool ensured;

        public static void Ensure()
        {
            if (ensured) return;
            string preferred = Data;
            try { VerifyWritable(preferred); }
            catch (Exception ex)
            {
                Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasterMiao", NameRules.ShortHash(Base), "Data");
                VerifyWritable(Data);
                StartupDiagnostic = "程序目录不可写，工作数据改存 / Application folder is not writable; data is stored at:\n" + Data + "\n" + ex.Message;
            }
            Directory.CreateDirectory(Templates);
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(Jobs);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Recovery);
            ensured = true;
        }

        internal static void VerifyWritable(string folder)
        {
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".write-check-" + Guid.NewGuid().ToString("N"));
            using (FileStream stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            { stream.WriteByte(0); stream.Flush(true); }
        }
    }

    public sealed class UserSettings
    {
        public string Language { get; set; }
        public bool AskLanguageOnStartup { get; set; }
        public int ListZoomPercent { get; set; }
        public string LastOutputRoot { get; set; }
        public string LastProjectPath { get; set; }
        public List<string> RecentProjects { get; set; }
        public int LocateShortcut { get; set; }
        public int DeduplicateShortcut { get; set; }

        public UserSettings()
        {
            Language = string.Empty;
            AskLanguageOnStartup = true;
            ListZoomPercent = 100;
            LastOutputRoot = string.Empty;
            LastProjectPath = string.Empty;
            RecentProjects = new List<string>();
        }
    }

    public static class UserSettingsStore
    {
        public static UserSettings Current { get; private set; }

        public static void Load()
        {
            try { Current = File.Exists(AppPaths.Settings) ? JsonFile.Load<UserSettings>(AppPaths.Settings) : new UserSettings(); }
            catch { Current = new UserSettings(); }
            if (Current.RecentProjects == null) Current.RecentProjects = new List<string>();
            if (Current.ListZoomPercent < 80 || Current.ListZoomPercent > 200) Current.ListZoomPercent = 100;
        }

        public static void Save()
        {
            if (Current == null) Current = new UserSettings();
            JsonFile.Save(AppPaths.Settings, Current);
        }

        public static void RememberProject(string path)
        {
            if (Current == null) Load();
            Current.LastProjectPath = path ?? string.Empty;
            Current.RecentProjects.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(path)) Current.RecentProjects.Insert(0, path);
            if (Current.RecentProjects.Count > 10) Current.RecentProjects.RemoveRange(10, Current.RecentProjects.Count - 10);
            Save();
        }
    }

    public static class JsonFile
    {
        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
        }

        public static T Load<T>(string path)
        {
            bool recovered;
            return Load<T>(path, out recovered);
        }

        public static T Load<T>(string path, out bool recovered)
        {
            recovered = false;
            try { return Read<T>(path); }
            catch (Exception primary)
            {
                if (!(primary is IOException) && !(primary is ArgumentException) && !(primary is InvalidOperationException)) throw;
                if (!File.Exists(path + ".bak")) throw;
                try { T result = Read<T>(path + ".bak"); recovered = true; return result; }
                catch { throw new IOException("项目及备份均无法读取 / Neither the document nor its backup could be read: " + path, primary); }
            }
        }

        private static T Read<T>(string path)
        {
            T result = CreateSerializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
            if (object.Equals(result, null)) throw new InvalidOperationException("JSON document contains no data.");
            return result;
        }

        public static void Save<T>(string path, T value) { SaveChecked(path, value, null); }

        internal static void SaveChecked<T>(string path, T value, string expectedHash)
        {
            path = Path.GetFullPath(path);
            string serialized = CreateSerializer().Serialize(value);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // A distinct temp file and a cross-process exclusive lock prevent interleaved writers.
            using (FileStream lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (expectedHash != null && !string.Equals(expectedHash, File.Exists(path) ? ProjectStore.ContentHash(path) : string.Empty, StringComparison.Ordinal))
                    throw new IOException("项目已被另一实例修改，请重新打开后再保存 / Another instance changed this project. Reopen it before saving.");
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(serialized);
                    using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                    if (File.Exists(path))
                    {
                        bool primaryValid = true;
                        try { Read<object>(path); } catch (ArgumentException) { primaryValid = false; } catch (InvalidOperationException) { primaryValid = false; }
                        // Replacing a damaged primary must not destroy the last good backup.
                        File.Replace(temporary, path, primaryValid ? path + ".bak" : null, true);
                    }
                    else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }

        public static T Clone<T>(T value)
        {
            JavaScriptSerializer serializer = CreateSerializer();
            return serializer.Deserialize<T>(serializer.Serialize(value));
        }
    }

    public static class NameRules
    {
        public static string SafeStem(string value, string fallback)
        {
            string stem = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
            stem = stem.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(stem)) stem = fallback;
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Any(item => string.Equals(item, stem, StringComparison.OrdinalIgnoreCase))) stem = "_" + stem;
            if (stem.Length > 120) stem = stem.Substring(0, 120).TrimEnd();
            return stem;
        }

        public static string ShortHash(string text)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return BitConverter.ToString(bytes, 0, 10).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    public static class CategoryRules
    {
        public static List<CategoryOption> BuildOptions(List<CategoryNode> nodes)
        {
            List<CategoryOption> result = new List<CategoryOption>();
            foreach (CategoryNode node in nodes.Where(item => item.Id != CategoryNode.RootId))
                result.Add(new CategoryOption { Id = node.Id, Path = UiText.IsEnglish && node.Id == CategoryNode.UnclassifiedId ? "Unclassified" : GetPath(nodes, node.Id) });
            return result.OrderBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string GetPath(List<CategoryNode> nodes, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id == CategoryNode.RootId) return string.Empty;
            List<string> parts = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            CategoryNode current = nodes.FirstOrDefault(item => item.Id == id);
            while (current != null && current.Id != CategoryNode.RootId && seen.Add(current.Id))
            {
                parts.Insert(0, current.Name);
                current = nodes.FirstOrDefault(item => item.Id == current.ParentId);
            }
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts.ToArray());
        }

        public static bool IsDescendant(List<CategoryNode> nodes, string possibleChildId, string ancestorId)
        {
            string currentId = possibleChildId;
            HashSet<string> seen = new HashSet<string>();
            while (!string.IsNullOrWhiteSpace(currentId) && seen.Add(currentId))
            {
                if (currentId == ancestorId) return true;
                CategoryNode node = nodes.FirstOrDefault(item => item.Id == currentId);
                currentId = node == null ? string.Empty : node.ParentId;
            }
            return false;
        }
    }
}
