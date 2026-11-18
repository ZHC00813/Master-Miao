using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace MasterMiao.Weld {
 public sealed class WeldExportOptions {public string Folder;public bool Step;}
 public sealed class WeldExportResult {public string Folder,Assembly,Step;public int Instances;}
 public sealed partial class WeldBridge {
  sealed class ExportMember {public string Path,Configuration,Instance;public double[] Transform;}
  static void SaveExportDoc(IModelDoc2 doc,string path){doc.ClearSelection2(true);int error=0,warning=0;if(!doc.Extension.SaveAs(path,0,1,null,ref error,ref warning)||error!=0||!File.Exists(path))throw new IOException("保存失败："+Path.GetFileName(path)+"（"+error+"）");}
  static string SafeName(string name){foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return name.Trim().TrimEnd('.');}
  public List<WeldExportResult> ExportGroups(IEnumerable<string> ids,WeldExportOptions options){
   Guard();if(options==null||string.IsNullOrWhiteSpace(options.Folder))throw new ArgumentException("请选择导出文件夹。");string root=Path.GetFullPath(options.Folder);var wanted=new HashSet<string>(ids);var groups=Project.Groups.Where(g=>wanted.Contains(g.Id)).ToArray();if(groups.Length==0||groups.Length!=wanted.Count)throw new InvalidOperationException("请选择有效的焊接组。");
   var snapshots=new Dictionary<string,List<ExportMember>>();foreach(var group in groups){if(group.Members.Count==0)throw new InvalidOperationException(group.Name+" 没有零件。");var members=new List<ExportMember>();foreach(var member in group.Members){var c=Resolve(member);if(c==null)throw new InvalidOperationException("无法解析："+member.Instance);var model=c.GetModelDoc2() as IModelDoc2;if(c.IsVirtual||model==null||model.GetType()!=1||!File.Exists(c.GetPathName()))throw new InvalidOperationException("请先将零件保存为外部文件并解析："+member.Instance);if(model.GetSaveFlag())throw new InvalidOperationException("请先保存零件的最新修改："+member.Instance);members.Add(new ExportMember{Path=c.GetPathName(),Configuration=c.ReferencedConfiguration,Instance=c.Name2,Transform=(double[])c.GetTotalTransform(false).ArrayData});}snapshots[group.Id]=members;}
   string template=App.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);if(!File.Exists(template)&&!string.IsNullOrWhiteSpace(template)){string fallback=Path.Combine(Path.GetDirectoryName(template),"gb_assembly.asmdot");if(File.Exists(fallback))template=fallback;}if(!File.Exists(template))throw new FileNotFoundException("请在 SolidWorks 中设置默认装配体模板。",template);
   Directory.CreateDirectory(root);var results=new List<WeldExportResult>();string original=Path.GetFileName(Project.AssemblyPath);
   try{foreach(var group in groups){var entries=snapshots[group.Id];string name=SafeName(WeldBadge.Label(group)+"_"+group.Name);string folder=Path.Combine(root,name);for(int suffix=2;Directory.Exists(folder);suffix++)folder=Path.Combine(root,name+"_"+suffix);Directory.CreateDirectory(folder);string destination=Path.Combine(folder,name+".SLDASM");IModelDoc2 created=null;string createdTitle=null;
    try{created=App.NewDocument(template,0,0,0) as IModelDoc2;if(created==null)throw new IOException("无法新建焊接组装配体。");createdTitle=created.GetTitle();var assembly=(IAssemblyDoc)created;var math=(IMathUtility)App.GetMathUtility();
     foreach(var entry in entries){var added=assembly.AddComponent5(entry.Path,0,"",true,entry.Configuration,0,0,0);if(added==null)throw new IOException("无法插入："+entry.Instance);if(added.ReferencedConfiguration!=entry.Configuration)added.ReferencedConfiguration=entry.Configuration;created.ClearSelection2(true);if(!added.Select4(false,null,false))throw new IOException("无法设置零件位置。");assembly.UnfixComponent();added.Transform2=(MathTransform)math.CreateTransform(entry.Transform);created.ClearSelection2(true);added.Select4(false,null,false);assembly.FixComponent();}
     created.ClearSelection2(true);created.ForceRebuild3(true);VerifyExport(created,entries);

     SaveExportDoc(created,destination);createdTitle=created.GetTitle();
     // Rebuild the group's independent metadata and decals against the exported instances.
     using(var exported=new WeldBridge(new WeldStore(Path.Combine(folder,"焊接组数据")),App)){exported.Attach();var targetGroup=WeldStore.AddGroup(exported.Project,group.Name,group.Color);targetGroup.BadgeNumber=WeldBadge.Label(group);targetGroup.MarkersVisible=group.MarkersVisible;targetGroup.Remark=group.Remark;foreach(var c in ((IAssemblyDoc)created).GetComponents(false) as object[]??new object[0])WeldStore.AddMember(targetGroup,exported.Describe((IComponent2)c));exported.Project.MarkerSizePoints=Project.MarkerSizePoints;exported.SyncMarkers();SaveExportDoc(created,destination);}
     App.CloseDoc(createdTitle);created=null;int openError=0,openWarning=0;created=App.OpenDoc6(destination,2,1,"",ref openError,ref openWarning);if(created==null||openError!=0)throw new IOException("导出装配体重新打开失败："+openError);createdTitle=created.GetTitle();VerifyExport(created,entries);if(created.Extension.GetDecalsCount()!=entries.Count)throw new IOException("导出装配体的标牌数量不一致。");string step=null;if(options.Step){step=Path.Combine(folder,name+".STEP");SaveExportDoc(created,step);}
     var report=new StringBuilder();report.AppendLine("焊接组："+group.Name+" / 标牌："+WeldBadge.Label(group));report.AppendLine("零件实例："+entries.Count);report.AppendLine("装配体引用源零件，不生成零件副本；请保留源文件及其路径。");report.AppendLine(options.Step?"STEP：已输出几何模型；STEP 不包含标牌贴图。":"STEP：未选择。");report.AppendLine("零件位置和配置已核对；新装配体以固定位置保存，不复制原配合关系。");foreach(var entry in entries)report.AppendLine(entry.Instance+"\t"+entry.Configuration);File.WriteAllText(Path.Combine(folder,"导出说明.txt"),report.ToString(),new UTF8Encoding(true));results.Add(new WeldExportResult{Folder=folder,Assembly=destination,Step=step,Instances=entries.Count});
    }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"导出未完成.txt"),ex.Message,new UTF8Encoding(true));throw;}finally{if(created!=null)App.CloseDoc(createdTitle??created.GetTitle());}
   }}finally{int error=0;App.ActivateDoc3(original,false,0,ref error);}return results;
  }
  static void VerifyExport(IModelDoc2 doc,List<ExportMember> expected){var components=(((IAssemblyDoc)doc).GetComponents(false) as object[]??new object[0]).Cast<IComponent2>().ToList();if(components.Count!=expected.Count)throw new IOException("导出零件数量不一致。");foreach(var source in expected){var match=components.FirstOrDefault(c=>string.Equals(c.GetPathName(),source.Path,StringComparison.OrdinalIgnoreCase)&&c.ReferencedConfiguration==source.Configuration&&((double[])c.GetTotalTransform(false).ArrayData).Zip(source.Transform,(a,b)=>Math.Abs(a-b)).All(d=>d<1e-8));if(match==null)throw new IOException("导出引用、位置或配置不一致："+source.Instance);components.Remove(match);}}
 }
}
