using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace MasterMiao.Weld {
 public sealed class BadgeCluster {
  public int Count,Columns,Rows;public double Height;
  public double Width{get{return Height*.75;}}
  public double ClusterWidth{get{return Columns*Width+(Columns-1)*Height*.12;}}
  public double ClusterHeight{get{return Rows*Height+(Rows-1)*Height*.12;}}
  public void Shrink(double factor){if(factor<=0||factor>1)throw new ArgumentOutOfRangeException("factor");Height*=factor;}
  public double[] Offset(int slot){if(slot<0||slot>=Count)throw new ArgumentOutOfRangeException("slot");int row=slot/Columns;int inRow=Math.Min(Columns,Count-row*Columns);return new[]{(slot%Columns-(inRow-1)/2d)*(Width+Height*.12),((Rows-1)/2d-row)*Height*1.12};}
  public static BadgeCluster Plan(int count,double volume,double area,double spanU,double spanV,double scale){
   if(count<1||new[]{volume,area,spanU,spanV,scale}.Any(x=>x<=0||double.IsNaN(x)||double.IsInfinity(x)))throw new ArgumentException("标牌排版需要有效的体积和表面尺寸。");
   BadgeCluster best=null;double desired=Math.Min(.010,Math.Pow(volume,1d/3)*.4*scale);
   for(int cols=1;cols<=count;cols++){int rows=(count+cols-1)/cols;double w=cols*.75+(cols-1)*.12,h=rows+(rows-1)*.12;double size=Math.Min(desired,Math.Min(Math.Sqrt(area*.25/(w*h)),Math.Min(spanU*.95/w,spanV*.95/h)));if(best==null||size>best.Height+1e-12)best=new BadgeCluster{Count=count,Columns=cols,Rows=rows,Height=size};}
   return best;
  }
 }
 public sealed partial class WeldBridge {
  string DecalPrefix(WeldGroup g,WeldMember m){return "MMW_"+g.Id+"_"+m.Id+"_";}
  IDecal[] OwnedDecals(WeldGroup g,WeldMember m){string prefix=DecalPrefix(g,m);return (Document.Extension.GetDecals() as object[]??new object[0]).Cast<IDecal>().Where(d=>Path.GetFileName(((IRenderMaterial)d).TextureFilename??"").StartsWith(prefix,StringComparison.Ordinal)).ToArray();}
  static double Dot(double[] a,double[] b){return a[0]*b[0]+a[1]*b[1]+a[2]*b[2];}
  static double[] Cross(double[] a,double[] b){return new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};}
  static double[] Unit(double[] a){double len=Math.Sqrt(Dot(a,a));return a.Select(v=>v/len).ToArray();}
  sealed class Placement {public double[] Center,U,V;public double Width,Height;public IFace2 Face;}
  Placement[] LocateBadges(IComponent2 component,int count){
   object info;var bodies=component.GetBodies3(0,out info) as object[];
   var allFaces=(bodies??new object[0]).Cast<IBody2>().SelectMany(b=>(b.GetFaces() as object[]??new object[0]).Cast<IFace2>()).OrderByDescending(f=>f.GetArea()).ToArray();
   var faces=allFaces.Take(1).ToArray();
   foreach(var face in faces){
    if(!((ISurface)face.GetSurface()).IsPlane())throw new InvalidOperationException("最大表面为曲面，暂不支持自动贴标；原标记已保留。");
    var body=(IBody2)face.GetBody();var mass=(double[])body.GetMassProperties(1);
    var n=Unit((double[])face.Normal);var u=Unit(Cross(Math.Abs(n[1])<.9?new[]{0d,1d,0d}:new[]{1d,0d,0d},n));var v=Unit(Cross(n,u));var box=(double[])face.GetBox();var origin=(double[])face.GetClosestPointOn((box[0]+box[3])/2,(box[1]+box[4])/2,(box[2]+box[5])/2);if(origin==null)continue;
    var corners=new List<double[]>();for(int i=0;i<8;i++)corners.Add(new[]{box[(i&1)==0?0:3]-origin[0],box[(i&2)==0?1:4]-origin[1],box[(i&4)==0?2:5]-origin[2]});double minU=corners.Min(p=>Dot(p,u)),maxU=corners.Max(p=>Dot(p,u)),minV=corners.Min(p=>Dot(p,v)),maxV=corners.Max(p=>Dot(p,v));double spanU=maxU-minU,spanV=maxV-minV;if(spanU<.0001||spanV<.0001)continue;
    // Search the trimmed face, including corners of each badge footprint, to avoid holes and cut-outs.
    double scale=Project.MarkerSizePoints>=10&&Project.MarkerSizePoints<=24?Project.MarkerSizePoints/12:1;
    // Component decals project through the component. Exclude every other front/side
    // surface from the entire footprint, including holes, steps and other bodies.
    // Back-facing planes cannot receive this single-sided projection.
    var exclusions=allFaces.Where(f=>f!=face).Where(f=>!((ISurface)f.GetSurface()).IsPlane()||Dot(Unit((double[])f.Normal),n)>-.999).Select(f=>{
     var b=(double[])f.GetBox();var pts=new List<double[]>();for(int i=0;i<8;i++)pts.Add(new[]{b[(i&1)==0?0:3]-origin[0],b[(i&2)==0?1:4]-origin[1],b[(i&4)==0?2:5]-origin[2]});return new[]{pts.Min(p=>Dot(p,u)),pts.Max(p=>Dot(p,u)),pts.Min(p=>Dot(p,v)),pts.Max(p=>Dot(p,v))};
    }).ToArray();
    var grid=BadgeCluster.Plan(count,mass[3],face.GetArea(),spanU,spanV,scale);
    var choices=new List<double[]>();double[] fractions={.5,.25,.75,.125,.875,.375,.625,.0625,.9375};
    for(int attempt=0;attempt<24&&choices.Count==0;attempt++){
     foreach(double fv in fractions){foreach(double fu in fractions){
      double cu=minU+spanU*fu,cv=minV+spanV*fv;
      const double clearance=1e-7;
      if(exclusions.Any(b=>cu+grid.ClusterWidth/2+clearance>=b[0]&&cu-grid.ClusterWidth/2-clearance<=b[1]&&cv+grid.ClusterHeight/2+clearance>=b[2]&&cv-grid.ClusterHeight/2-clearance<=b[3]))continue;
      var clusterCenter=Enumerable.Range(0,3).Select(k=>origin[k]+u[k]*(minU+spanU*fu)+v[k]*(minV+spanV*fv)).ToArray();bool fits=true;
      // Test the whole compact cluster, including gaps, against the trimmed face.
      for(int ix=0;ix<=16&&fits;ix++)for(int iy=0;iy<=16;iy++){
       var q=Enumerable.Range(0,3).Select(k=>clusterCenter[k]+u[k]*grid.ClusterWidth*(ix/16d-.5)+v[k]*grid.ClusterHeight*(iy/16d-.5)).ToArray();var nearest=face.GetClosestPointOn(q[0],q[1],q[2]) as double[];
       if(nearest==null||Enumerable.Range(0,3).Sum(k=>(nearest[k]-q[k])*(nearest[k]-q[k]))>1e-14){fits=false;break;}
      }
      if(!fits)continue;
      for(int index=0;index<count;index++){var offset=grid.Offset(index);choices.Add(Enumerable.Range(0,3).Select(k=>clusterCenter[k]+u[k]*offset[0]+v[k]*offset[1]).ToArray());}
      break;
     }if(choices.Count>0)break;}
     if(choices.Count==0)grid.Shrink(.8);
    }
    double width=grid.Width,height=grid.Height;
    if(choices.Count!=count)continue;
    // A component-bound decal uses the part's local frame, not assembly coordinates.
    return choices.Select(point=>new Placement{Center=point,U=u,V=v,Width=width,Height=height,Face=face}).ToArray();
   }throw new InvalidOperationException("零件“"+component.Name2+"”没有足够的平面空间放置标牌，已保留原标记。");
  }
  public int SyncMarkers(){
   Guard();int missing=0;Document.ClearSelection2(true);var layouts=new Dictionary<string,Placement[]>();
   foreach(var member in Project.Groups.SelectMany(g=>g.Members).GroupBy(m=>m.Id).Select(g=>g.First())){var component=Resolve(member);if(component==null)continue;int count=Project.Groups.Count(g=>g.Members.Any(m=>m.Id==member.Id));layouts[member.Id]=LocateBadges(component,count);}
   foreach(var group in Project.Groups)foreach(var member in group.Members){var component=Resolve(member);if(component==null){missing++;continue;}var all=Project.Groups.Where(g=>g.Members.Any(m=>m.Id==member.Id)).ToArray();int slot=Array.FindIndex(all,g=>g.Id==group.Id);var placement=layouts[member.Id][slot];string label=WeldBadge.ValidateNumber(WeldBadge.Label(group));string asset=Path.Combine(store.Root,"Assets",DecalPrefix(group,member)+group.Color.ToString("X6")+"_"+label+".png");if(!File.Exists(asset)||!File.Exists(Path.ChangeExtension(asset,"mask.png")))WeldBadge.Save(asset,label,group.Color);
    var previous=OwnedDecals(group,member);var previousIds=previous.Select(d=>d.DecalID).ToArray();var decal=previous.FirstOrDefault(d=>string.Equals(((IRenderMaterial)d).TextureFilename,asset,StringComparison.OrdinalIgnoreCase));bool created=decal==null;if(created)decal=Document.Extension.CreateDecal();var material=(IRenderMaterial)decal;
    if(created){string template=Path.Combine((Directory.Exists(App.GetExecutablePath())?App.GetExecutablePath():Path.GetDirectoryName(App.GetExecutablePath())),"data","graphics","Decals","Logos","sw.p2d");if(!File.Exists(template))throw new FileNotFoundException("SolidWorks 标牌贴图模板缺失。",template);material.FileName=template;material.TextureFilename=asset;if(!material.AddEntity(component))throw new IOException("无法将标牌绑定到零件实例。");}
    material.MappingType=1;material.FixedAspectRatio=false;material.FitWidth=false;material.FitHeight=false;material.SetCenterPoint2(placement.Center[0],placement.Center[1],placement.Center[2]);material.SetUDirection2(placement.U[0],placement.U[1],placement.U[2]);material.SetVDirection2(placement.V[0],placement.V[1],placement.V[2]);material.Width=placement.Width;material.Height=placement.Height;material.DoubleSided=false;material.Transparency=0;decal.MaskType=1;decal.MaskFilename=Path.ChangeExtension(asset,"mask.png");decal.MaskInvert=false;
    int id=decal.DecalID;if(created&&!Document.Extension.AddDecal((Decal)decal,out id))throw new IOException("SolidWorks 未能创建标牌贴图。");
    if(!Document.Extension.HideDecal(id,!group.MarkersVisible))throw new IOException("无法更新标牌显示状态。");
    foreach(int oldId in previousIds)if(oldId!=id&&!Document.Extension.DeleteDecal(oldId,true))throw new IOException("旧标牌清理失败，请刷新后重试。");
    var annotation=FindMarker(MarkerName(group,member));if(annotation!=null){Document.ClearSelection2(true);if(!annotation.Select3(false,null)||!Document.Extension.DeleteSelection2(0))throw new IOException("旧标记清理失败，请刷新后重试。");}
    member.Marker=DecalPrefix(group,member);member.Position=placement.Center;
   }
   Document.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStoreImagesWithModel,true);if(Project.Groups.Any(g=>g.MarkersVisible))Document.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayDecals,true);Document.Extension.UpdateRenderMaterialsInSceneGraph(true);Document.GraphicsRedraw2();Save();return missing;
  }
 }
}
