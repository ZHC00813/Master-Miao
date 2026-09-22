using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SWBodyOrganizer;
class VerifySourceRecovery {
 static void Check(bool ok,string text) { if(!ok) throw new Exception(text); Console.WriteLine("PASS "+text); }
 static void Restore(SourceRecord a,SourceRecord b,AppProject p) { typeof(MainForm).GetMethod("RestoreScanEdits",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{a,b,p.Categories}); }
 static int Main(string[] args) {
  string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
  AppProject p=ProjectStore.Load(Path.GetFullPath(args[2]));
  SourceRecord old=p.Sources.Single();
  if(args[0]=="prepare") {
   var reordered=JsonFile.Clone(old); reordered.Bodies.Reverse();for(int i=0;i<reordered.Bodies.Count;i++){reordered.Bodies[i].Index=i;reordered.Bodies[i].ExportName="reset";}
   Restore(old,reordered,p);Check(reordered.Bodies.All(b=>b.ExportName==old.Bodies.Single(o=>o.PersistReference==b.PersistReference).ExportName),"names survive reordered bodies");
   var named=JsonFile.Clone(reordered);foreach(var b in named.Bodies){b.PersistReference="changed-"+b.Index;b.ExportName="reset";}Restore(old,named,p);Check(named.Bodies.All(b=>b.ExportName!="reset"),"name and geometry fallback after reference changes");
   var changed=JsonFile.Clone(old);foreach(var b in changed.Bodies){b.GeometryEvidenceKey="changed";b.ExportName="reset";}Restore(old,changed,p);Check(changed.Bodies.All(b=>b.ExportName=="reset"),"changed geometry is not silently matched");
   var ambiguous=JsonFile.Clone(old); ambiguous.Bodies.Add(JsonFile.Clone(ambiguous.Bodies[0]));foreach(var b in ambiguous.Bodies){b.PersistReference="";b.ExportName="reset";}Restore(old,ambiguous,p);Check(ambiguous.Bodies[0].ExportName=="reset" && ambiguous.Bodies.Last().ExportName=="reset","ambiguous identities do not inherit edits");
   string id=old.Id; ProjectStore.PrepareSourceRelink(old,args[3]);Check(old.Id==id&&old.Status!="读取完成"&&old.Bodies.All(b=>b.ExportName!=""),"relink retains source ID and edits but blocks stale export");
   ProjectStore.Save(Path.Combine(root,"relinked.swbody.json"),p);
   var r=new WorkerRequest{Operation="scan",SaveSourcesBeforeScan=true,GeneratePreviews=true,KeepSourceDocumentsOpen=true,CacheRoot=Path.Combine(root,"cache"),CheckpointPath=Path.Combine(root,"scan-checkpoint.json")};r.Sources.Add(new SourceRecord{Id=old.Id,Path=old.Path,Name=old.Name});JsonFile.Save(Path.Combine(root,"scan-request.json"),r);
  } else {
   var scan=JsonFile.Load<WorkerResponse>(Path.Combine(root,"scan-response.json"));Check(scan.Success,"actual saved-source rescan");var fresh=scan.Sources.Single();Restore(old,fresh,p);Check(fresh.Bodies.Count==7&&fresh.Bodies.All(b=>old.Bodies.Any(o=>o.ExportName==b.ExportName)),"all seven real names retained after save and relink");p.Sources[0]=fresh;
   var r=new WorkerRequest{Operation="export",CacheRoot=Path.Combine(root,"cache"),StagingRoot=Path.Combine(root,"staging"),OutputRoot=Path.Combine(root,"output"),CheckpointPath=Path.Combine(root,"export-checkpoint.json"),ExportSettings=new ExportSettings{ExportSldprt=true,ExportStep=true,CreateAssembly=true,SeparateStepOutput=true,ConflictPolicy="自动编号"}};
   foreach(var b in fresh.Bodies) r.ExportItems.Add(new ExportPlanItem{BodyId=b.Id,SourcePath=b.SourcePath,SourceName=b.SourceName,BodyIndex=b.Index,OriginalName=b.OriginalName,ExportName=b.ExportName,CategoryPath="STEP",SourceSha256=b.SourceSha256,Configuration=b.Configuration,PersistReference=b.PersistReference,GeometryKey=b.GeometryKey,GeometryEvidenceKey=b.GeometryEvidenceKey,GeometryBounds=b.GeometryBounds,Volume=b.Volume,SurfaceArea=b.SurfaceArea,Quantity=1});r.Sources=p.Sources;p.OutputRoot=r.OutputRoot;p.Export=r.ExportSettings;ProjectStore.Save(Path.Combine(root,"verified.swbody.json"),p);JsonFile.Save(Path.Combine(root,"export-request.json"),r);
  }return 0;
 }
}
