using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SWBodyOrganizer;

internal static class PrepareIntegration
{
    private static int Main(string[] args)
    {
        string operation = args[0], root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        if (operation == "report")
        {
            WorkerResponse response = JsonFile.Load<WorkerResponse>(Path.Combine(root, "export-response.json"));
            Type writer = typeof(AppProject).Assembly.GetType("SWBodyOrganizer.ExcelReportWriter", true);
            writer.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] {
                Path.Combine(root, "actual-export-report.xlsx"), response.ExportResults,
                "V1.2.6 SolidWorks 2024 integration test", Path.Combine(root, "output"), "zh-CN" });
            return 0;
        }
        WorkerRequest request = new WorkerRequest {
            Operation = operation, CacheRoot = Path.Combine(root, "cache"),
            StagingRoot = Path.Combine(root, "staging"), OutputRoot = Path.Combine(root, "output"),
            CancelFile = Path.Combine(root, "cancel.signal"),
            CheckpointPath = Path.Combine(root, operation + "-checkpoint.json"),
            KeepSourceDocumentsOpen = true, GeneratePreviews = true
        };
        if (operation == "scan")
            request.Sources.Add(new SourceRecord { Path = Path.GetFullPath(args[2]), Name = Path.GetFileNameWithoutExtension(args[2]) });
        else
        {
            WorkerResponse scan = JsonFile.Load<WorkerResponse>(Path.Combine(root, "scan-response.json"));
            if (!scan.Success) throw new InvalidOperationException("The current scan did not pass.");
            AppProject project = new AppProject { Name = "V1.2.6 real integration", Sources = scan.Sources, OutputRoot = request.OutputRoot };
            CategoryNode category = new CategoryNode { Name = "Integration Parts", ParentId = CategoryNode.RootId };
            project.Categories.Add(category);
            List<BodyRecord> bodies = project.AllBodies().ToList();
            int[] selected = new[] { 0, bodies.Count / 3, bodies.Count - 1 }.Distinct().ToArray();
            foreach (BodyRecord body in bodies) body.ExportSelected = false;
            foreach (int index in selected)
            {
                BodyRecord body = bodies[index];
                body.ExportSelected = true;
                body.ExportName = "Miao-" + (index + 1).ToString("D3");
                body.CategoryId = category.Id;
                request.ExportItems.Add(new ExportPlanItem {
                    BodyId = body.Id, SourcePath = body.SourcePath, SourceName = body.SourceName,
                    BodyIndex = body.Index, OriginalName = body.OriginalName, ExportName = body.ExportName,
                    CategoryPath = category.Name, PreviewFront = body.PreviewFront, PreviewTop = body.PreviewTop, PreviewIso = body.PreviewIso,
                    SourceSha256 = body.SourceSha256, Configuration = body.Configuration, PersistReference = body.PersistReference,
                    GeometryKey = body.GeometryKey, GeometryEvidenceKey = body.GeometryEvidenceKey,
                    GeometryBounds = body.GeometryBounds, Volume = body.Volume, SurfaceArea = body.SurfaceArea,
                    Quantity = 1, Occurrences = new List<string> { body.SourcePath + " | " + body.OriginalName }
                });
            }
            request.Sources = project.Sources;
            request.ExportSettings = new ExportSettings { ExportSldprt = true, ExportStep = true, SeparateStepOutput = true,
                CreateAssembly = true, CreateExcel = true, ConflictPolicy = "自动编号" };
            project.Export = JsonFile.Clone(request.ExportSettings);
            ProjectStore.Save(Path.Combine(root, "integration.swbody.json"), project);
        }
        JsonFile.Save(Path.Combine(root, operation + "-request.json"), request);
        Console.WriteLine("Prepared " + operation + " request in " + root);
        return 0;
    }
}
