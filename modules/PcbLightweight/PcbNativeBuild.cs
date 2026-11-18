using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment=System.Environment;

class PcbNativeBuild {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
 static ISldWorks sw; static string root=AppDomain.CurrentDomain.BaseDirectory,folder,manifestPath;
 static bool ownsInstance; static readonly List<IModelDoc2> opened=new List<IModelDoc2>();
 [DllImport("ole32.dll")]static extern int GetRunningObjectTable(int reserved,out IRunningObjectTable table);
 [DllImport("ole32.dll")]static extern int CreateBindCtx(int reserved,out IBindCtx context);
 static IEnumerable<Dictionary<string,object>> Rows(object value){return ((IEnumerable)value).Cast<Dictionary<string,object>>();}
 static string Text(Dictionary<string,object> value,string key){return Convert.ToString(value[key]);}
 static Dictionary<string,object> Read(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
 static void CheckCanceled(){if(folder!=null&&File.Exists(Path.Combine(folder,"cancel.flag")))throw new OperationCanceledException("PCB task canceled; original files preserved");}
 static void Write(string path,object value){File.WriteAllText(path,Json.Serialize(value),new UTF8Encoding(false));}
 static string Hash(string path){using(var file=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
 static ISldWorks Attach(int pid){
  IRunningObjectTable rot;IBindCtx context;GetRunningObjectTable(0,out rot);CreateBindCtx(0,out context);
  IEnumMoniker list;rot.EnumRunning(out list);var item=new IMoniker[1];
  while(list.Next(1,item,IntPtr.Zero)==0){string name;item[0].GetDisplayName(context,null,out name);if(name.IndexOf("SolidWorks",StringComparison.OrdinalIgnoreCase)<0)continue;
   try{object value;rot.GetObject(item[0],out value);var app=value as ISldWorks;if(app!=null&&app.GetProcessID()==pid)return app;}catch(COMException){}
  }return null;
 }
 static IBody2[] Bodies(IModelDoc2 doc,int kind=0){return (((IPartDoc)doc).GetBodies2(kind,false) as object[]??new object[0]).Cast<IBody2>().ToArray();}
 static double Volume(IBody2 body){return ((double[])body.GetMassProperties(1))[3]*1e9;}
 static double Volume(IModelDoc2 doc){return Bodies(doc).Sum(b=>Volume(b));}
 static int Faults(IBody2 body){var faults=body.Check3;return faults==null?0:faults.Count;}
 static int Faults(IModelDoc2 doc){return Bodies(doc).Sum(b=>Faults(b));}
 static void Close(IModelDoc2 doc){if(doc==null)return;sw.CloseDoc(doc.GetTitle());opened.Remove(doc);}
 static IModelDoc2 Load(string path){
  CheckCanceled();
  int error=0;var data=sw.GetImportFileData(path) as IImportStepData;if(data!=null)data.MapConfigurationData=false;
  Console.WriteLine("IMPORT "+Path.GetFileName(path));var doc=sw.LoadFile4(path,"r",data,ref error);
  if(doc!=null)opened.Add(doc);if(doc==null||!(doc is IPartDoc))throw new Exception("STEP import failed: "+error);
  return doc;
 }
 static void Save(IModelDoc2 doc,string path){CheckCanceled();int error=0,warning=0;if(!doc.Extension.SaveAs(path,0,1,null,ref error,ref warning)||error!=0)throw new Exception("Save failed: "+error+" "+path);}
 static IModelDoc2 Part(IEnumerable<IBody2> bodies){
  string template=sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
  var doc=sw.NewDocument(template,0,0,0) as IModelDoc2;if(doc==null)throw new Exception("Cannot create native part");opened.Add(doc);
  foreach(var body in bodies)if(((IPartDoc)doc).CreateFeatureFromBody3(body.Copy2(false),false,0)==null)throw new Exception("Cannot insert validated body");
  return doc;
 }
 static List<IBody2> LoadGroups(object groups){
  var result=new List<IBody2>();
  foreach(var group in Rows(groups)){
   var doc=Load(Text(group,"path"));var bodies=Bodies(doc);int expected=Convert.ToInt32(group["count"]);
   if(bodies.Length!=expected||Bodies(doc,1).Length!=0||Faults(doc)!=0)throw new Exception("Invalid prepared bodies: "+Text(group,"path"));
   var copies=bodies.Select(b=>(IBody2)b.Copy2(false)).ToList();Close(doc);
   if(group.ContainsKey("kind")&&Text(group,"kind")=="cut"){
    var current=copies.OrderByDescending(b=>Volume(b)).First();copies.Remove(current);
    foreach(var tool in copies){int error;var output=current.Operations2(15902,tool,out error) as object[];
     if(error!=0||output==null||output.Length!=1)throw new Exception("Cavity reconstruction failed: "+error);current=(IBody2)output[0];
    }
    if(Faults(current)!=0||Math.Abs(Volume(current)-Convert.ToDouble(group["volume_mm3"]))>.001)throw new Exception("Cavity geometry changed");
    result.Add(current);
   }else result.AddRange(copies);
  }return result;
 }
 static string Quote(string text){return "\""+text.Replace("\"","\\\"")+"\"";}
 static void Python(string arguments){
  string python=Path.Combine(root,"PcbRuntime","python.exe"),script=Path.Combine(root,"PcbEngine","scripts","native_geometry.py"),libs=Path.Combine(root,"PcbRuntime","cadlibs");
  var info=new ProcessStartInfo(python,Quote(script)+" --ocp-path "+Quote(libs)+" "+arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  info.EnvironmentVariables["PYTHONUTF8"]="1";
  using(var process=Process.Start(info)){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();process.WaitForExit();Console.WriteLine(stdout.Result);Console.WriteLine(stderr.Result);if(process.ExitCode!=0)throw new Exception("Geometry preparation or retention audit failed");}
 }
 static void Run(Dictionary<string,object> manifest){
  string output=Text(manifest,"output"),native=Path.ChangeExtension(output,"SLDPRT");
  if(File.Exists(output)||File.Exists(native))throw new Exception("Choose a new output name; existing files are preserved");
  var all=LoadGroups(manifest["groups"]);var current=all.OrderByDescending(b=>Volume(b)).First();all.Remove(current);
  var connectionReports=new List<object>();int repairRound=0;
  while(all.Count>0){int progress=0;Console.WriteLine("UNION pending="+all.Count);
   for(int i=all.Count-1;i>=0;i--){CheckCanceled();int error;var candidate=((IBody2)current.Copy2(false)).Operations2(15903,all[i].Copy2(false),out error) as object[];
    if(error!=0||candidate==null||candidate.Length!=1)continue;var joined=(IBody2)candidate[0];if(Faults(joined)!=0)continue;
    if(Volume(joined)+.001<Math.Max(Volume(current),Volume(all[i])))throw new Exception("Union lost material");
    current=joined;all.RemoveAt(i);progress++;if(all.Count%20==0)Console.WriteLine("REMAINING "+all.Count);
   }
   if(progress>0)continue;
   if(++repairRound>2)throw new Exception("Cannot safely connect all bodies within the micro-connection limits");
   string pending=Path.Combine(folder,"pending-"+repairRound+".stp");var doc=Part(new[]{current}.Concat(all));Save(doc,pending);Close(doc);
   string work=Path.Combine(folder,"contacts-"+repairRound);Python("contacts --input "+Quote(pending)+" --work "+Quote(work));
   var contacts=Read(Path.Combine(work,"contacts.json"));
   if(Convert.ToDouble(contacts["upper_added_volume_mm3"])>Math.Max(1,Volume(current)*.0002))throw new Exception("Connection material exceeds budget");
   all.AddRange(LoadGroups(contacts["groups"]));connectionReports.Add(contacts["connections"]);
  }
  if(Faults(current)!=0)throw new Exception("Native union is invalid");
  var final=Part(new[]{current});string step=null;bool verified=false;double repairDelta=0;
  for(int round=0;round<3;round++){
   step=Path.Combine(folder,"roundtrip-"+round+".stp");Save(final,step);Close(final);final=Load(step);
   if(Bodies(final).Length!=1||Bodies(final,1).Length!=0)throw new Exception("STEP conversion split the solid");
   int faults=Faults(final);Console.WriteLine("ROUNDTRIP "+round+" faults="+faults);
   if(faults==0){verified=true;break;}
   double before=Volume(final);var beforeBounds=(double[])Bodies(final)[0].GetBodyBox();
   ((IPartDoc)final).ImportDiagnosis(true,false,true,0);double delta=Volume(final)-before;repairDelta+=Math.Abs(delta);
   if(Bodies(final).Length!=1||Bodies(final,1).Length!=0||Faults(final)!=0||repairDelta>.05
     ||((double[])Bodies(final)[0].GetBodyBox()).Zip(beforeBounds,(x,y)=>Math.Abs(x-y)).Max()>1e-6)
    throw new Exception("STEP boundary repair failed or exceeded its geometry budget");
  }
  if(!verified)throw new Exception("STEP roundtrip did not converge");
  string part=Path.Combine(folder,"verified.SLDPRT");Save(final,part);double volume=Volume(final);Close(final);
  int e=0,w=0;final=sw.OpenDoc6(part,1,3,"",ref e,ref w);if(final!=null)opened.Add(final);
  if(final==null||Bodies(final).Length!=1||Bodies(final,1).Length!=0||Faults(final)!=0||Math.Abs(Volume(final)-volume)>.001)throw new Exception("Native save/reopen verification failed");
  Close(final);Python("audit --input "+Quote(step)+" --manifest "+Quote(manifestPath)+" --work "+Quote(folder));
  var report=Read(Path.Combine(folder,"geometry-audit.json"));
  CheckCanceled();File.Copy(step,output,false);File.Copy(part,native,false);
  report["solidworks_verified"]=true;report["nativeFaults"]=0;report["reopened"]=true;report["output"]=output;report["native_part"]=native;
  report["input_sha256"]=manifest["input_sha256"];report["output_sha256"]=Hash(output);report["native_sha256"]=Hash(native);
  if(manifest.ContainsKey("simplification_mode"))report["simplification_mode"]=manifest["simplification_mode"];if(manifest.ContainsKey("omitted_components"))report["omitted_components"]=manifest["omitted_components"];report["kept"]=manifest["kept"];report["simplified"]=manifest["simplified"];report["connections"]=connectionReports;report["boundary_repair_volume_mm3"]=repairDelta;
  Write(output+".report.json",report);Console.WriteLine("PASS VERIFIED STEP AND SLDPRT "+output);
 }
 static void RunMulti(Dictionary<string,object> manifest){
  string output=Text(manifest,"output"),native=Path.ChangeExtension(output,"SLDPRT");
  if(File.Exists(output)||File.Exists(native))throw new Exception("Output exists");
  var all=LoadGroups(manifest["groups"]);int count=all.Count;double original=all.Sum(b=>Volume(b));var doc=Part(all);string step=null;bool verified=false;double delta=0;
  for(int round=0;round<3;round++){
   step=Path.Combine(folder,"multi-roundtrip-"+Guid.NewGuid().ToString("N")+".stp");Save(doc,step);Close(doc);doc=Load(step);
   if(Bodies(doc).Length!=count||Bodies(doc,1).Length!=0)throw new Exception("Multi-body STEP count changed");
   if(Faults(doc)==0){verified=true;break;}
   double before=Volume(doc);((IPartDoc)doc).ImportDiagnosis(true,false,true,0);delta+=Math.Abs(Volume(doc)-before);
   if(Bodies(doc).Length!=count||Bodies(doc,1).Length!=0||Faults(doc)!=0||delta>.05)throw new Exception("Multi-body boundary repair exceeded limits");
  }
  if(!verified||Math.Abs(Volume(doc)-original)>.05)throw new Exception("Multi-body geometry verification failed");
  string part=Path.Combine(folder,"multi-verified-"+Guid.NewGuid().ToString("N")+".SLDPRT");double volume=Volume(doc);Save(doc,part);Close(doc);int e=0,w=0;doc=sw.OpenDoc6(part,1,1,"",ref e,ref w);if(doc!=null)opened.Add(doc);
  if(doc==null||Bodies(doc).Length!=count||Bodies(doc,1).Length!=0||Faults(doc)!=0||Math.Abs(Volume(doc)-volume)>.001)throw new Exception("Multi-body native reopen failed");Close(doc);
  Python("audit --multi --input "+Quote(step)+" --manifest "+Quote(manifestPath)+" --work "+Quote(folder));
  var report=Read(Path.Combine(folder,"geometry-audit.json"));report["solidworks_verified"]=true;report["nativeFaults"]=0;report["reopened"]=true;report["boundary_repair_volume_mm3"]=delta;
  File.Copy(step,output,false);File.Copy(part,native,false);Write(output+".report.json",report);Console.WriteLine("PASS MULTI STEP AND SLDPRT count="+count);
 }
 static void ConvertPart(Dictionary<string,object> manifest){
  string input=Text(manifest,"input"),target=Text(manifest,"output");
  if(!string.Equals(Path.GetExtension(target),".SLDPRT",StringComparison.OrdinalIgnoreCase)||File.Exists(target))throw new Exception("Choose a new SLDPRT output filename");
  var doc=Load(input);int count=Bodies(doc).Length;double volume=Volume(doc);
  Console.WriteLine("IMPORTED solids="+count+" surfaces="+Bodies(doc,1).Length+" faults="+Faults(doc));
  if(count==0||Bodies(doc,1).Length!=0)throw new Exception("Imported PCB geometry failed validation");
  if(Faults(doc)!=0){
   var bounds=Bodies(doc).Select(b=>(double[])b.GetBodyBox()).ToArray();
   ((IPartDoc)doc).ImportDiagnosis(true,false,true,0);
   var after=Bodies(doc).Select(b=>(double[])b.GetBodyBox()).ToArray();
   double change=Math.Abs(Volume(doc)-volume);
   if(after.Length!=bounds.Length||Bodies(doc,1).Length!=0||Faults(doc)!=0||change>.05||bounds.Where((b,i)=>b.Zip(after[i],(x,y)=>Math.Abs(x-y)).Max()>1e-6).Any())throw new Exception("Bounded PCB boundary repair failed");
   Console.WriteLine("BOUNDARY REPAIR delta_mm3="+change);volume=Volume(doc);
  }
  Save(doc,target);Close(doc);int error=0,warning=0;doc=sw.OpenDoc6(target,1,1,"",ref error,ref warning);if(doc!=null)opened.Add(doc);
  if(doc==null||Bodies(doc).Length!=count||Bodies(doc,1).Length!=0||Faults(doc)!=0||Math.Abs(Volume(doc)-volume)>Math.Max(1e-6,volume*1e-8))throw new Exception("Native part save/reopen validation failed");
  Close(doc);Write(target+".verification.json",new{solids=count,volume_mm3=volume,nativeFaults=0,reopened=true,input_sha256=Hash(input),output_sha256=Hash(target)});
  Console.WriteLine("PASS CONVERT SAVE REOPEN "+target);
 }
 [STAThread]static int Main(string[] args){int brep=0,knit=0,surface=0;bool multi=false,interconnect=false,preferences=false;string template=null;
  try{
   manifestPath=Path.GetFullPath(args[0]);var manifest=Read(manifestPath);folder=Text(manifest,"folder");CheckCanceled();
   int pid;
   if(args.Length==3&&args[1]=="--pid")pid=int.Parse(args[2]);
   else{
    string exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SOLIDWORKS Corp","SOLIDWORKS","SLDWORKS.exe");
    foreach(var running in Process.GetProcessesByName("SLDWORKS"))try{string found=running.MainModule.FileName;if(File.Exists(found)){exe=found;break;}}catch{}
    if(!File.Exists(exe))throw new Exception("SolidWorks installation not found");
    var process=Process.Start(new ProcessStartInfo(exe){WindowStyle=ProcessWindowStyle.Hidden});pid=process.Id;ownsInstance=true;
   }
   for(int attempt=0;attempt<60&&sw==null;attempt++){sw=Attach(pid);if(sw==null)Thread.Sleep(1000);}
   if(sw==null)throw new Exception("SolidWorks did not become available");
   Console.WriteLine("NATIVE WORKER PID "+pid);
   brep=sw.GetUserPreferenceIntegerValue(207);knit=sw.GetUserPreferenceIntegerValue(577);surface=sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swCreateBodyFromSurfacesOption);
   multi=sw.GetUserPreferenceToggle(220);interconnect=sw.GetUserPreferenceToggle(691);template=sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);preferences=true;
   string fallback=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SOLIDWORKS","SOLIDWORKS 2024","templates","gb_part.prtdot");
   if(!File.Exists(template)){
    if(!File.Exists(fallback)){
     string baseFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SOLIDWORKS");
     if(Directory.Exists(baseFolder))foreach(var version in Directory.GetDirectories(baseFolder).OrderByDescending(x=>x)){
      string templates=Path.Combine(version,"templates");if(!Directory.Exists(templates))continue;
      fallback=Directory.GetFiles(templates,"*.prtdot").FirstOrDefault();if(fallback!=null)break;
     }
    }
    if(!File.Exists(fallback))throw new Exception("Configure a valid SolidWorks part template");sw.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart,fallback);
   }
   sw.SetUserPreferenceIntegerValue(207,0);sw.SetUserPreferenceIntegerValue(577,0);sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swCreateBodyFromSurfacesOption,(int)swGeneralImportSurfaceSolidEntityOptions_e.swGeneralImportByBrep);sw.SetUserPreferenceToggle(220,false);sw.SetUserPreferenceToggle(691,false);
   if(manifest.ContainsKey("operation")&&Text(manifest,"operation")=="build-multi")RunMulti(manifest);else if(manifest.ContainsKey("operation")&&Text(manifest,"operation")=="convert-part")ConvertPart(manifest);else Run(manifest);return 0;
  }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}
  finally{
   if(sw!=null){foreach(var doc in opened.ToArray())try{Close(doc);}catch{}
    if(preferences)try{sw.SetUserPreferenceIntegerValue(207,brep);sw.SetUserPreferenceIntegerValue(577,knit);sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swCreateBodyFromSurfacesOption,surface);sw.SetUserPreferenceToggle(220,multi);sw.SetUserPreferenceToggle(691,interconnect);sw.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart,template);}catch{}
    if(ownsInstance)try{sw.ExitApp();}catch{}
   }
  }
 }
}
