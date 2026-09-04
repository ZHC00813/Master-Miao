using System;
using System.Collections;
using System.IO;
using System.Reflection;

internal static class VerifyStepFolderLayout
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: VerifyStepFolderLayout <MasterMiao.exe>");
            return 2;
        }

        string scratch = Path.Combine(Path.GetTempPath(), "MasterMiao.StepLayout." + Guid.NewGuid().ToString("N"));
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type requestType = assembly.GetType("SWBodyOrganizer.WorkerRequest", true);
            Type settingsType = assembly.GetType("SWBodyOrganizer.ExportSettings", true);
            Type workerType = assembly.GetType("SWBodyOrganizer.WorkerMain", true);
            object request = Activator.CreateInstance(requestType);
            object settings = Activator.CreateInstance(settingsType);
            requestType.GetProperty("OutputRoot").SetValue(request, scratch, null);
            requestType.GetProperty("ExportSettings").SetValue(request, settings, null);

            MethodInfo partRootMethod = workerType.GetMethod("GetPartOutputRoot", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo stepRootMethod = workerType.GetMethod("GetStepOutputRoot", BindingFlags.Static | BindingFlags.NonPublic);
            if (partRootMethod == null || stepRootMethod == null) throw new MissingMethodException("Folder layout methods were not found.");

            AssertPath(scratch, Convert.ToString(partRootMethod.Invoke(null, new[] { request })), "same-folder part root");
            AssertPath(scratch, Convert.ToString(stepRootMethod.Invoke(null, new[] { request })), "same-folder STEP root");

            settingsType.GetProperty("SeparateStepOutput").SetValue(settings, true, null);
            string partRoot = Convert.ToString(partRootMethod.Invoke(null, new[] { request }));
            string stepRoot = Convert.ToString(stepRootMethod.Invoke(null, new[] { request }));
            AssertPath(Path.Combine(scratch, "零件源文件"), partRoot, "separate part root");
            AssertPath(Path.Combine(scratch, "STEP生产文件"), stepRoot, "separate STEP root");
            if (string.Equals(partRoot, stepRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Separate roots overlap.");

            Directory.CreateDirectory(partRoot);
            Directory.CreateDirectory(stepRoot);
            settingsType.GetProperty("ExportSldprt").SetValue(settings, true, null);
            settingsType.GetProperty("ExportStep").SetValue(settings, true, null);
            settingsType.GetProperty("ConflictPolicy").SetValue(settings, "自动编号", null);
            MethodInfo stemMethod = workerType.GetMethod("ResolveOutputStem", BindingFlags.Static | BindingFlags.NonPublic);
            if (stemMethod == null) throw new MissingMethodException("Output stem resolver was not found.");
            File.WriteAllText(Path.Combine(stepRoot, "测试件.STEP"), "existing");
            string numbered = Convert.ToString(stemMethod.Invoke(null, new object[] { partRoot, stepRoot, "测试件", settings }));
            if (numbered != "测试件_2") throw new InvalidOperationException("STEP conflict was not detected across the mirrored root.");

            VerifyCommitIntoProductionTree(assembly, request, settings, scratch, partRoot, stepRoot);

            Console.WriteLine("PASS: same-folder layout, separate mirrored roots, cross-root auto-numbering, and STEP commit routing.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try
            {
                string full = Path.GetFullPath(scratch);
                string temp = Path.GetFullPath(Path.GetTempPath());
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
            }
            catch { }
        }
    }

    private static void AssertPath(string expected, string actual, string label)
    {
        if (!string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(label + " mismatch: " + actual);
    }

    private static void VerifyCommitIntoProductionTree(Assembly assembly, object request, object settings, string scratch, string partRoot, string stepRoot)
    {
        Type resultType = assembly.GetType("SWBodyOrganizer.ExportResultItem", true);
        Type assemblyResultType = assembly.GetType("SWBodyOrganizer.AssemblyResultItem", true);
        Type exporterType = assembly.GetType("SWBodyOrganizer.AssemblyStepExporter", true);
        Type jobType = assembly.GetType("SWBodyOrganizer.AssemblyStepExporter+StepJob", true);
        object part = Activator.CreateInstance(resultType);
        object assemblyResult = Activator.CreateInstance(assemblyResultType);
        object job = Activator.CreateInstance(jobType, true);

        string category = Path.Combine("结构件", "板件");
        string finalPartFolder = Path.Combine(partRoot, category);
        string finalStepFolder = Path.Combine(stepRoot, category);
        string stageFolder = Path.Combine(scratch, "stage");
        Directory.CreateDirectory(finalPartFolder);
        Directory.CreateDirectory(finalStepFolder);
        Directory.CreateDirectory(stageFolder);
        string sldprtPath = Path.Combine(finalPartFolder, "归位测试件.SLDPRT");
        string stepPath = Path.Combine(finalStepFolder, "归位测试件.STEP");
        string stagePart = Path.Combine(stageFolder, "归位测试件.STEP");
        string stageAssembly = Path.Combine(stageFolder, "assembly.STEP");
        File.WriteAllText(stagePart, "ISO-10303-21;\r\nEND-ISO-10303-21;");
        File.WriteAllText(stageAssembly, "ISO-10303-21;\r\nEND-ISO-10303-21;");

        resultType.GetProperty("SldprtPath").SetValue(part, sldprtPath, null);
        resultType.GetProperty("StepPath").SetValue(part, stepPath, null);
        resultType.GetProperty("StepStatus").SetValue(part, "待批量导出", null);
        assemblyResultType.GetProperty("SourcePath").SetValue(assemblyResult, Path.Combine(scratch, "主文件.SLDPRT"), null);
        assemblyResultType.GetProperty("AssemblyPath").SetValue(assemblyResult, Path.Combine(partRoot, "主文件_拆分装配体.SLDASM"), null);

        IList parts = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(resultType));
        parts.Add(part);
        jobType.GetField("Assembly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(job, assemblyResult);
        jobType.GetField("Parts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(job, parts);
        jobType.GetField("StageFolder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(job, stageFolder);
        jobType.GetField("StageAssemblyStep", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(job, stageAssembly);

        settings.GetType().GetProperty("ConflictPolicy").SetValue(settings, "跳过", null);
        MethodInfo commit = exporterType.GetMethod("CommitJob", BindingFlags.Static | BindingFlags.NonPublic);
        if (commit == null) throw new MissingMethodException("STEP commit method was not found.");
        commit.Invoke(null, new[] { request, job });

        if (!File.Exists(stepPath)) throw new InvalidOperationException("Part STEP was not committed to the production tree.");
        if (File.Exists(Path.Combine(finalPartFolder, "归位测试件.STEP"))) throw new InvalidOperationException("Part STEP leaked into the SLDPRT tree.");
        string assemblyStepPath = Convert.ToString(assemblyResultType.GetProperty("AssemblyStepPath").GetValue(assemblyResult, null));
        AssertPath(Path.Combine(stepRoot, "主文件_拆分装配体.STEP"), assemblyStepPath, "assembly STEP destination");
        if (!File.Exists(assemblyStepPath)) throw new InvalidOperationException("Assembly STEP was not committed to the production root.");
    }
}
