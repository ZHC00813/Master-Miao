using SolidWorks.Interop.swconst;using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;using System.Web.Script.Serialization;
class SwVerify{
static int Count(IModelDoc2 d,int kind){return (((IPartDoc)d).GetBodies2(kind,false) as object[]??new object[0]).Length;}
static double Vol(IModelDoc2 d){return (((IPartDoc)d).GetBodies2(0,false) as object[]??new object[0]).Cast<IBody2>().Sum(b=>((double[])b.GetMassProperties(1))[3]);}
static int Faults(IModelDoc2 d){return (((IPartDoc)d).GetBodies2(0,false) as object[]??new object[0]).Cast<IBody2>().Sum(b=>{var faults=b.Check3;return faults==null?0:faults.Count;});}
static string Sha256(string path){using(var stream=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
[STAThread]static int Main(string[] a){ISldWorks sw=null;IModelDoc2 owned=null;try{
sw=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var prior=(sw.GetDocuments() as object[]??new object[0]).Cast<IModelDoc2>().Select(oldDoc=>new {Doc=oldDoc,Name=oldDoc.GetTitle(),Dirty=oldDoc.GetSaveFlag()}).ToList();
if(a.Length==2&&a[0]=="--open-native"){
 string native=Path.GetFullPath(a[1]);if(!File.Exists(native)||!string.Equals(Path.GetExtension(native),".SLDPRT",StringComparison.OrdinalIgnoreCase))throw new Exception("Native part not found");
 int error=0,warning=0;var existing=prior.FirstOrDefault(item=>string.Equals(item.Doc.GetPathName(),native,StringComparison.OrdinalIgnoreCase));
 var document=existing==null?sw.OpenDoc6(native,1,1,"",ref error,ref warning):existing.Doc;if(existing==null)owned=document;
 if(document==null)throw new Exception("Open native part failed: "+error);
 sw.ActivateDoc3(document.GetTitle(),false,1,ref error);Console.WriteLine("OPENED NATIVE PART "+native);return 0;
}
string input=Path.GetFullPath(a[0]),target=Path.Combine(Path.GetDirectoryName(input),Path.GetFileNameWithoutExtension(input)+"_SW单实体.SLDPRT");if(File.Exists(target))throw new Exception("Output already exists");int e=0,w=0;
int brep=sw.GetUserPreferenceIntegerValue(207),knit=sw.GetUserPreferenceIntegerValue(577),surfaceOption=sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swCreateBodyFromSurfacesOption);
bool multi=sw.GetUserPreferenceToggle(220),interconnect=sw.GetUserPreferenceToggle(691);
string template=sw.GetUserPreferenceStringValue(8),fallback=@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot";
bool fixTemplate=!File.Exists(template);if(fixTemplate&&!File.Exists(fallback))throw new Exception("Please configure a valid SolidWorks part template first");
IModelDoc2 d;
try{
 Console.WriteLine("SET BREP="+sw.SetUserPreferenceIntegerValue(207,0));Console.WriteLine("SET BODY="+sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swCreateBodyFromSurfacesOption,(int)swGeneralImportSurfaceSolidEntityOptions_e.swGeneralImportByBrep));sw.SetUserPreferenceIntegerValue(577,0);sw.SetUserPreferenceToggle(220,false);sw.SetUserPreferenceToggle(691,false);
 if(fixTemplate)sw.SetUserPreferenceStringValue(8,fallback);
 var data=sw.GetImportFileData(input) as IImportStepData;if(data!=null)data.MapConfigurationData=false;
 Console.WriteLine("IMPORT START "+DateTime.Now.ToString("o"));d=sw.LoadFile4(input,"r",data,ref e);owned=d;
}finally{
 if(fixTemplate)sw.SetUserPreferenceStringValue(8,template);
 sw.SetUserPreferenceToggle(691,interconnect);sw.SetUserPreferenceIntegerValue(207,brep);sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swCreateBodyFromSurfacesOption,surfaceOption);sw.SetUserPreferenceIntegerValue(577,knit);sw.SetUserPreferenceToggle(220,multi);
}Console.WriteLine("RETURNED "+(d==null?"null":d.GetTitle()+" | "+d.GetPathName()));if(d==null)throw new Exception("Import failed "+e);if(!(d is IPartDoc))throw new Exception("Expected part");
int solids=Count(d,0),surfaces=Count(d,1);double volume=Vol(d);Console.WriteLine("IMPORTED solids="+solids+" surfaces="+surfaces+" volume="+volume);if(solids!=1||surfaces!=0)throw new Exception("Unexpected bodies");
int faultsBefore=Faults(d);bool repaired=false;
if(faultsBefore>0){
 var faces=new List<object>();
 foreach(IBody2 body in ((IPartDoc)d).GetBodies2(0,false) as object[]??new object[0]){
  var fault=body.Check3;if(fault==null)continue;
  for(int i=0;i<fault.Count;i++){
   var face=fault.get_Entity2(i) as IFace2;
   var bounds=face==null?null:((double[])face.GetBox()).Select(v=>v*1000).ToArray();
   faces.Add(new{code=fault.get_ErrorCode(i),bounds_mm=bounds});
  }
 }
 string hash;using(var stream=File.OpenRead(input))using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
 File.WriteAllText(input+".sw-faults.json",new JavaScriptSerializer().Serialize(new{source_sha256=hash,verified=false,nativeFaults=faultsBefore,faces=faces}),System.Text.Encoding.UTF8);
 Console.WriteLine("NATIVE_GEOMETRY_FAILED "+faultsBefore);
 sw.CloseDoc(d.GetTitle());owned=null;return 2;
}if(!d.Extension.SaveAs(target,0,1,null,ref e,ref w)||e!=0)throw new Exception("Save failed "+e);
sw.CloseDoc(d.GetTitle());owned=null;d=sw.OpenDoc6(target,1,1,"",ref e,ref w);owned=d;if(d==null||Count(d,0)!=solids||Count(d,1)!=0||Faults(d)!=0||Math.Abs(Vol(d)-volume)>1e-10)throw new Exception("Reopen verification failed");
foreach(var old in prior)if(old.Doc.GetSaveFlag()!=old.Dirty)throw new Exception("Existing document save flag changed: "+old.Name);
sw.ActivateDoc3(d.GetTitle(),false,1,ref e);d.ShowNamedView2("*Isometric",7);d.ViewZoomtofit2();
File.WriteAllText(input+".sw-verification.json",new JavaScriptSerializer().Serialize(new {input,target,source_sha256=Sha256(input),target_sha256=Sha256(target),solids,surfaces,volume_m3=volume,reopened=true,nativeFaults=0,repaired=repaired,existingDocumentFlagsUnchanged=true,time=DateTime.Now.ToString("o")}),System.Text.Encoding.UTF8);
Console.WriteLine("PASS SAVE REOPEN "+target);return 0;
}catch(Exception x){Console.WriteLine("FAIL "+x);if(sw!=null&&owned!=null)try{sw.CloseDoc(owned.GetTitle());}catch(Exception cleanup){Console.WriteLine("Test document cleanup failed: "+cleanup.Message);}return 1;}}
}
