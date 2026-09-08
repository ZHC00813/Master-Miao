using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SWBodyOrganizer;

// Compile against Models.cs + ProjectStore.cs; no SolidWorks or desktop session is needed.
namespace SWBodyOrganizer { internal static class UiText { public static bool IsEnglish { get { return false; } } } }

internal static class VerifyProjectStorage
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    private static AppProject Sample(string preview)
    {
        SourceRecord source = new SourceRecord { Name = "source.SLDPRT", Path = "external-source.SLDPRT" };
        source.Bodies.Add(new BodyRecord { SourceId = source.Id, ExportName = "中文零件", PreviewIso = preview });
        AppProject project = new AppProject();
        project.Sources.Add(source);
        return project;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--recovery-child")
        {
            string record = ProjectStore.SaveRecovery(Sample(string.Empty));
            File.WriteAllText(args[1], record);
            Thread.Sleep(6000);
            return 0;
        }
        string root = Path.GetFullPath(args.Length == 0 ? Path.Combine(Path.GetTempPath(), "MasterMiao-storage-" + Guid.NewGuid().ToString("N")) : args[0]);
        Directory.CreateDirectory(root);
        try
        {
            string image = Path.Combine(root, "preview.png");
            File.WriteAllBytes(image, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2 });
            AppProject project = Sample(image);
            project.LastTaskRequest = new WorkerRequest();
            project.LastTaskRequest.ExportItems.Add(new ExportPlanItem { PreviewIso = image, ExportName = "retry part" });
            project.LastTaskResponse = new WorkerResponse();
            project.LastTaskResponse.ExportResults.Add(new ExportResultItem { PreviewIso = image, StepStatus = "失败", Message = "test failure" });
            string original = Path.Combine(root, "original", "project.swbody.json");
            ProjectStore.Save(original, project);
            AppProject raw = JsonFile.Load<AppProject>(original);
            string relative = raw.AllBodies().First().PreviewIso;
            Check(!Path.IsPathRooted(relative) && relative.StartsWith("Previews"), "saved preview paths are project-relative");
            Check(project.AllBodies().First().PreviewIso == image, "saving does not mutate the live preview path");
            string asset = Path.Combine(Path.GetDirectoryName(original), relative);
            DateTime copiedAt = File.GetLastWriteTimeUtc(asset);
            ProjectStore.Save(original, project);
            Check(copiedAt == File.GetLastWriteTimeUtc(asset), "unchanged previews are not recopied");
            string movedFolder = Path.Combine(root, "moved");
            Directory.Move(Path.GetDirectoryName(original), movedFolder);
            string moved = Path.Combine(movedFolder, "project.swbody.json");
            AppProject loaded = ProjectStore.Load(moved);
            Check(File.Exists(loaded.AllBodies().First().PreviewIso), "moving the whole project retains previews");
            Check(loaded.LastTaskRequest.ExportItems[0].ExportName == "retry part" && loaded.LastTaskResponse.ExportResults[0].StepStatus == "失败" &&
                File.Exists(loaded.LastTaskRequest.ExportItems[0].PreviewIso) && File.Exists(loaded.LastTaskResponse.ExportResults[0].PreviewIso), "moved project preserves retry snapshots and resolves their thumbnails");
            Check(loaded.Sources[0].Path == project.Sources[0].Path, "source CAD remains an external reference");

            string legacy = Path.Combine(movedFolder, "legacy.swbody.json");
            string escapedOld = Path.Combine(Path.GetDirectoryName(original), relative).Replace("\\", "\\\\");
            File.WriteAllText(legacy, "{\"SchemaVersion\":2,\"Sources\":[{\"Id\":\"old-source\",\"Path\":\"original.SLDPRT\",\"Bodies\":[{\"Id\":\"old-body\",\"PreviewIso\":\"" + escapedOld + "\"}]}]}");
            AppProject old = ProjectStore.Load(legacy);
            Check(old.SchemaVersion == 3 && old.Export.ExportSldprt && !old.Export.SeparateStepOutput && old.ProjectId.Length > 0, "Schema 2 migrates with safe defaults");
            Check(File.Exists(old.AllBodies().First().PreviewIso) && old.AllBodies().First().PreviewIso.StartsWith(movedFolder), "moved Schema 2 absolute previews resolve inside Previews");
            string future = Path.Combine(root, "future.swbody.json");
            File.WriteAllText(future, "{\"SchemaVersion\":99}");
            bool futureRejected = false;
            try { ProjectStore.Load(future); } catch (InvalidDataException) { futureRejected = true; }
            Check(futureRejected, "unknown future project schema is rejected");

            loaded.Name = "valid previous";
            ProjectStore.Save(moved, loaded);
            loaded.Name = "valid current";
            ProjectStore.Save(moved, loaded);
            string beforeLock = ProjectStore.ContentHash(moved);
            bool failed = false;
            using (FileStream lockFile = new FileStream(moved, FileMode.Open, FileAccess.Read, FileShare.None))
            { try { ProjectStore.Save(moved, loaded); } catch (IOException) { failed = true; } }
            Check(failed && beforeLock == ProjectStore.ContentHash(moved), "locked primary keeps the complete old project");

            File.WriteAllText(moved, "{broken");
            AppProject recovered = ProjectStore.Load(moved);
            Check(recovered.Name == "valid previous" && !string.IsNullOrEmpty(ProjectStore.LastLoadWarning), "corrupt primary falls back to valid backup with diagnostic");
            string backupHash = ProjectStore.ContentHash(moved + ".bak");
            ProjectStore.Save(moved, recovered);
            Check(backupHash == ProjectStore.ContentHash(moved + ".bak"), "repairing a corrupt primary preserves the valid backup");

            AppProject concurrent = ProjectStore.Load(moved);
            AppProject external = JsonFile.Clone(concurrent);
            external.Name = "other instance";
            JsonFile.Save(moved, external);
            failed = false;
            try { ProjectStore.Save(moved, concurrent); } catch (IOException) { failed = true; }
            Check(failed && JsonFile.Load<AppProject>(moved).Name == "other instance", "stale writer cannot overwrite an external saved revision");
            string committedPreview = ProjectStore.ResolvePreview(movedFolder, JsonFile.Load<AppProject>(moved).AllBodies().First().PreviewIso);
            string committedPreviewHash = ProjectStore.ContentHash(committedPreview);
            string changedImage = Path.Combine(root, "changed-preview.png");
            File.WriteAllBytes(changedImage, new byte[] { 3, 4, 5, 6, 7 });
            concurrent.AllBodies().First().PreviewIso = changedImage;
            failed = false;
            try { ProjectStore.Save(moved, concurrent); } catch (IOException) { failed = true; }
            Check(failed && ProjectStore.ContentHash(committedPreview) == committedPreviewHash, "stale save cannot replace committed preview assets");
            AppProject previewRevision = ProjectStore.Load(moved);
            previewRevision.AllBodies().First().PreviewIso = changedImage;
            ProjectStore.Save(moved, previewRevision);
            string backupPreview = ProjectStore.ResolvePreview(movedFolder, JsonFile.Load<AppProject>(moved + ".bak").AllBodies().First().PreviewIso);
            Check(ProjectStore.ContentHash(backupPreview) == committedPreviewHash, "successful preview changes preserve assets referenced by the previous backup");
            using (FileStream lease = new FileStream(moved + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                failed = false;
                try { JsonFile.Save(moved, external); } catch (IOException) { failed = true; }
                Check(failed, "simultaneous JSON writers are rejected through an exclusive lock");
            }
            string blocked = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blocked, "unchanged");
            DateTime savedTime = recovered.LastSavedUtc;
            failed = false;
            try { ProjectStore.Save(Path.Combine(blocked, "project.json"), recovered); } catch (IOException) { failed = true; }
            Check(failed && File.ReadAllText(blocked) == "unchanged" && recovered.LastSavedUtc == savedTime, "failed save leaves old files and successful-save timestamp unchanged");

            string cad = Path.Combine(root, "source.SLDPRT");
            string relinked = Path.Combine(root, "same.SLDPRT");
            File.WriteAllText(cad, "source identity fixture"); File.Copy(cad, relinked);
            SourceRecord identity = new SourceRecord { Path = cad, ContentSha256 = ProjectStore.ContentHash(cad) };
            Check(ProjectStore.RebindSource(identity, relinked), "byte-identical source relocation is accepted");
            File.WriteAllText(cad, "different design");
            failed = false;
            try { ProjectStore.RebindSource(identity, cad); } catch (InvalidDataException) { failed = true; }
            Check(failed && identity.Path == relinked, "different content cannot steal existing body identities");
            Check(!ProjectStore.RebindSource(new SourceRecord(), cad), "legacy sources without hashes require a rescan");

            AppProject one = Sample(string.Empty), two = Sample(string.Empty);
            string oneRecovery = ProjectStore.SaveRecovery(one), twoRecovery = ProjectStore.SaveRecovery(two);
            Check(oneRecovery != twoRecovery && File.Exists(oneRecovery) && File.Exists(twoRecovery), "unsaved projects get independent recovery records");
            string marker = Path.Combine(root, "child-path.txt");
            ProcessStartInfo start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--recovery-child \"" + marker + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using (Process child = Process.Start(start))
            {
                for (int i = 0; i < 50 && !File.Exists(marker); i++) Thread.Sleep(50);
                if (!File.Exists(marker)) throw new Exception("Recovery child did not start.");
                string childPath = File.ReadAllText(marker);
                Check(!ProjectStore.GetRecoveryFiles().Contains(childPath), "running foreign instances are excluded from recovery offers");
                if (!child.WaitForExit(10000)) throw new Exception("Recovery child did not finish.");
                Check(ProjectStore.GetRecoveryFiles().Contains(childPath), "closed instances retain recoverable projects");
                ProjectStore.DeleteRecovery(one);
                Check(!File.Exists(oneRecovery) && File.Exists(twoRecovery) && File.Exists(childPath), "cleanup cannot delete another project or instance recovery");
                AppProject resumed = ProjectStore.Load(childPath);
                ProjectStore.AdoptRecovery(childPath, resumed);
                ProjectStore.Save(Path.Combine(root, "resumed", "project.swbody.json"), resumed);
                ProjectStore.DeleteRecovery(resumed);
                Check(!File.Exists(childPath) && File.Exists(twoRecovery), "saved resumed work retires only the chosen inactive recovery");
            }
            Console.WriteLine("PASS " + checks + " filesystem checks; artifacts: " + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
