using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SWBodyOrganizer;
class VerifySourceGuards {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 [STAThread] static int Main(string[] args) {
  var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var asm=typeof(MainForm).Assembly;
  try {asm.GetType("SWBodyOrganizer.SourceDocuments").GetMethod("FindOpen",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{app,args[0]});throw new Exception("conflict was not rejected");}
  catch(TargetInvocationException e){Check(e.InnerException.Message.Contains("同名")&&e.InnerException.Message.Contains(args[0])&&e.InnerException.Message.Contains("重新关联"),"same-name conflict identifies path and recovery action");}
  int part=(int)swUserPreferenceStringValue_e.swDefaultTemplatePart,assembly=(int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly,toggle=(int)swUserPreferenceToggle_e.swAlwaysUseDefaultTemplates;
  string p=app.GetUserPreferenceStringValue(part),a=app.GetUserPreferenceStringValue(assembly);bool t=app.GetUserPreferenceToggle(toggle);
  using(var scope=(IDisposable)Activator.CreateInstance(asm.GetType("SWBodyOrganizer.ImportTemplateScope"),new object[]{app})) {Check(File.Exists(app.GetUserPreferenceStringValue(part))&&File.Exists(app.GetUserPreferenceStringValue(assembly))&&app.GetUserPreferenceToggle(toggle),"temporary import templates valid");}
  Check(p==app.GetUserPreferenceStringValue(part)&&a==app.GetUserPreferenceStringValue(assembly)&&t==app.GetUserPreferenceToggle(toggle),"user template settings restored");
  var m=app.GetOpenDocumentByName(args[1]) as IModelDoc2;Check(m!=null&&!m.GetSaveFlag(),"saved test source clean after export");m.SetSaveFlag();
  try {asm.GetType("SWBodyOrganizer.ExportIntegrity").GetMethod("VerifyMemory",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{m,m.ConfigurationManager.ActiveConfiguration.Name});throw new Exception("dirty source accepted");}
  catch(TargetInvocationException e){Check(e.InnerException.Message.Contains("保存源文件并重读"),"dirty source still blocked with actionable recovery");}
  int errors=0,warnings=0;Check(m.Save3(1,ref errors,ref warnings)&&errors==0,"test-only dirty state saved");
  return 0;
 }
}
