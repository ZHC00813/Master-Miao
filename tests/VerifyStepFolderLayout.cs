using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SWBodyOrganizer;

// Filesystem/plan tests only. Header strings are deliberately NOT valid CAD fixtures.
internal static class VerifyStepFolderLayout
{
    private static Assembly app;
    private static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "MasterMiao.Integrity." + Guid.NewGuid().ToString("N"));
        try
        {
            app = typeof(WorkerRequest).Assembly;
            Directory.CreateDirectory(scratch);
            WorkerRequest request = new WorkerRequest { OutputRoot = scratch };
            Assert((string)Invoke("WorkerMain", "GetPartOutputRoot", request) == scratch, "same part root");
            Assert((string)Invoke("WorkerMain", "GetStepOutputRoot", request) == scratch, "same STEP root");
            request.ExportSettings.SeparateStepOutput = true;
            request.ExportSettings.ExportStep = true;
            request.ExportSettings.CreateAssembly = true;
            request.ExportSettings.ConflictPolicy = "自动编号";
            string partRoot = Path.Combine(scratch, "零件源文件"), stepRoot = Path.Combine(scratch, "STEP生产文件");
            Directory.CreateDirectory(Path.Combine(stepRoot, "A"));
            File.WriteAllText(Path.Combine(stepRoot, "A", "件.STEP"), "existing STEP sentinel");
            List<ExportResultItem> results = new List<ExportResultItem>
            {
                new ExportResultItem { BodyId="1", SourcePath=Path.Combine(scratch,"source.SLDPRT"), PlannedExportName="件", CategoryPath="A" },
                new ExportResultItem { BodyId="2", SourcePath=Path.Combine(scratch,"source.SLDPRT"), PlannedExportName="件_2", CategoryPath="B" },
                new ExportResultItem { BodyId="3", SourcePath=Path.Combine(scratch,"source.SLDPRT"), PlannedExportName="件", CategoryPath="B" }
            };
            Invoke("WorkerMain", "PlanOutputPaths", request, results);
            Assert(results[0].ExportName == "件_2", "disk conflict across mirrored roots");
            Assert(results.Select(x=>x.ExportName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "global uniqueness AFTER numbering across categories");
            Assert(results.All(x=>x.SldprtPath.StartsWith(partRoot) && x.StepPath.StartsWith(stepRoot)), "mirrored routing");
            Assert(results[0].AssemblyStepPath.StartsWith(stepRoot), "planned assembly STEP root");
            request.ExportSettings.ConflictPolicy = "跳过";
            Throws(()=>Invoke("WorkerMain", "PlanOutputPaths", request, results), "same-task collisions cannot become unverified reuse");

            string source = Path.Combine(scratch, "source.SLDPRT"), staged = Path.Combine(scratch,"stage.bin"), target=Path.Combine(scratch,"target.bin");
            File.WriteAllText(source, "user source"); File.WriteAllText(staged,"new complete output"); File.WriteAllText(target,"old output");
            string hash=(string)Invoke("ExportIntegrity","FileHash",source);
            Invoke("ExportIntegrity","VerifySourceFile",source,hash);
            using (FileStream solidWorksLock = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                Assert((string)Invoke("ExportIntegrity", "FileHash", source) == hash, "read-only hash works with an existing writable CAD handle");
            Assert(File.ReadAllText(source) == "user source", "hash never writes source bytes");
            using (FileStream exclusiveLock = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Throws(() => Invoke("ExportIntegrity", "FileHash", source), "exclusive writer remains protected");
            File.WriteAllText(source,"user change");
            Throws(()=>Invoke("ExportIntegrity","VerifySourceFile",source,hash),"content change rejects source independently of metadata");
            Throws(()=>Invoke("ExportIntegrity","CommitFile",staged,source,true,new[]{source}),"source cannot be overwritten");
            Throws(()=>Invoke("ExportIntegrity","CommitFile",Path.Combine(scratch,"missing"),target,true,new[]{source}),"missing staged copy fails");
            Assert(File.ReadAllText(target)=="old output","copy failure preserves previous destination");
            Invoke("ExportIntegrity","CommitFile",staged,target,true,new[]{source});
            Assert(File.ReadAllText(target)=="new complete output","committed complete copy");
            Assert(Directory.GetFiles(Path.Combine(scratch,".MasterMiao-backups")).Any(path=>File.ReadAllText(path)=="old output"),"overwrite retains old bytes");
            Assert(!(bool)Invoke("WorkerMain","IsSuccessful","跳过（未验证）"),"unverified skip is not success");

            double[] identity = {1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0};
            double[] mirror=(double[])identity.Clone(); mirror[0]=-1;
            double[] translated=(double[])identity.Clone(); translated[9]=0.1;
            double[] shear=(double[])identity.Clone(); shear[0]=2; shear[4]=.5;
            Assert((bool)Invoke("ExportIntegrity","IdentityTransform",identity),"identity placement accepted");
            Assert(!(bool)Invoke("ExportIntegrity","ProperRigidTransform",mirror),"reflection rejected");
            Assert(!(bool)Invoke("ExportIntegrity","ProperRigidTransform",shear),"determinant-one nonrigid transform rejected");
            Assert(!(bool)Invoke("ExportIntegrity","IdentityTransform",translated),"assembly displacement rejected");
            Assert((bool)Invoke("ExportIntegrity","ProperRigidTransform",translated),"duplicate congruence may allow translation without accepting wrong export placement");
            string incomplete=Path.Combine(scratch,"truncated.STEP"), empty=Path.Combine(scratch,"empty.STEP");
            File.WriteAllText(incomplete,"ISO-10303-21;\nHEADER;");
            File.WriteAllText(empty,"ISO-10303-21;\nEND-ISO-10303-21;");
            Assert(!(bool)Invoke("AssemblyStepExporter","IsValidStep",incomplete),"truncated format rejected");
            Assert((bool)Invoke("AssemblyStepExporter","IsValidStep",empty),"framing-only check is explicitly not geometry validation");
            Throws(()=>Invoke("ExportIntegrity","VerifyStep",null,empty,new List<ExportResultItem>(),null,null),"header-only file cannot get geometric success without SolidWorks");
            Console.WriteLine("PASS: mirrored/global final paths, source hash, collisions, atomic backup/failure, skip status, transforms, truncated STEP and header-only fail-closed checks. No SolidWorks geometry claim.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            string safe=Path.GetFullPath(scratch), temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(safe.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(safe).StartsWith("MasterMiao.Integrity.")&&Directory.Exists(safe)) Directory.Delete(safe,true);
        }
    }
    private static object Invoke(string type,string method,params object[] args)
    {
        return app.GetType("SWBodyOrganizer."+type,true).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,args);
    }
    private static void Assert(bool condition,string label) { if(!condition) throw new Exception("FAIL: "+label); }
    private static void Throws(Action action,string label)
    {
        try { action(); } catch(TargetInvocationException error) { if(error.InnerException is IOException || error.InnerException is InvalidDataException || error.InnerException is InvalidOperationException) return; throw; } catch(IOException) { return; }
        throw new Exception("FAIL: expected rejection: "+label);
    }
}
