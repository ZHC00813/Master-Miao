using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using Blankedge.SWHoleTools.Utilities;
using Blankedge.SWHoleTools.BatchExporter.Model;
namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    public static class ManufacturingMeasurements
    {
        private static double Dot(double[] a,double[] b) => a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
        private static double Span(IBody2 body,double[] n)
        {
            double x,y,z,u,v,w;
            if(!body.GetExtremePoint(n[0],n[1],n[2],out x,out y,out z) || !body.GetExtremePoint(-n[0],-n[1],-n[2],out u,out v,out w)) throw new InvalidOperationException("实体精确尺寸读取失败。");
            return Math.Abs((x-u)*n[0]+(y-v)*n[1]+(z-w)*n[2]);
        }
        private static string Mm(double d) => (d*1000).ToString("0.###");
        public static string Describe(IModelDoc2 model,ProductionCategory category)
        {
            if(!(model is IPartDoc part)) return "";
            var bodies=SwMath.ToComList<IBody2>(part.GetBodies2(0,false)).ToList();
            if(bodies.Count!=1) return "";
            var body=bodies[0]; var faces=SwMath.ToComList<IFace2>(body.GetFaces()).ToList();
            if(category==ProductionCategory.轴件)
            {
                var cylinders=faces.Where(f=>((ISurface)f.GetSurface()).IsCylinder()).OrderByDescending(f=>f.GetArea()).ToList();
                if(cylinders.Count==0)return "";
                var c=(double[])((ISurface)cylinders[0].GetSurface()).CylinderParams;
                double[] axis={c[3],c[4],c[5]};
                double radius=cylinders.Max(f=>((double[])((ISurface)f.GetSurface()).CylinderParams)[6]);
                return "轴向总长 "+Mm(Span(body,axis))+" mm；最大外径 Ø"+Mm(radius*2)+" mm";
            }
            var planes=faces.Where(f=>((ISurface)f.GetSurface()).IsPlane()).OrderByDescending(f=>f.GetArea()).Select(f=>(double[])((ISurface)f.GetSurface()).PlaneParams).ToList();
            if(planes.Count==0)return "";
            double[] n=planes[0].Take(3).ToArray();
            if(category==ProductionCategory.木板件 || category==ProductionCategory.钣金切割件) return "板厚 "+Mm(Span(body,n))+" mm";
            if(category==ProductionCategory.钣金折弯件)
            {
                var gaps=new List<double>();
                for(int i=0;i<planes.Count;i++)for(int j=i+1;j<planes.Count;j++)
                {
                    var a=planes[i];var b=planes[j];
                    if(Math.Abs(Math.Abs(Dot(a,b))-1)>1e-6)continue;
                    double gap=Math.Abs(a[0]*(a[3]-b[3])+a[1]*(a[4]-b[4])+a[2]*(a[5]-b[5]));
                    if(gap>=.0005 && gap<=.01)gaps.Add(Math.Round(gap,5));
                }
                var ranked=gaps.GroupBy(g=>g).OrderByDescending(g=>g.Count()).ToArray();
                if(ranked.Length>0 && ranked[0].Count()>=2 && (ranked.Length==1 || ranked[0].Count()>ranked[1].Count()))return "板厚（平行壁面测量） "+Mm(ranked[0].Key)+" mm";
                return "";
            }
            if(category!=ProductionCategory.管件)return "";
            var other=planes.FirstOrDefault(p=>Math.Abs(Dot(n,p))<1e-6);
            if(other==null)return "";
            double[] m=other.Take(3).ToArray();
            double[] axis2={n[1]*m[2]-n[2]*m[1],n[2]*m[0]-n[0]*m[2],n[0]*m[1]-n[1]*m[0]};
            var spans=new[]{Tuple.Create(Span(body,n),n),Tuple.Create(Span(body,m),m),Tuple.Create(Span(body,axis2),axis2)}.OrderByDescending(x=>x.Item1).ToArray();
            string text="轴向总长 "+Mm(spans[0].Item1)+" mm；截面 "+Mm(spans[1].Item1)+" × "+Mm(spans[2].Item1)+" mm";
            var walls=new List<double>();
            foreach(var cross in spans.Skip(1))
            {
                var stations=planes.Where(p=>Math.Abs(Math.Abs(Dot(cross.Item2,p))-1)<1e-6).Select(p=>Math.Round(Dot(cross.Item2,p.Skip(3).Take(3).ToArray()),6)).Distinct().OrderBy(v=>v).ToArray();
                if(stations.Length==4){walls.Add(stations[1]-stations[0]);walls.Add(stations[3]-stations[2]);}
            }
            if(walls.Count==4 && walls.Min()>.0005 && walls.Max()-walls.Min()<.00005) text+="；壁厚 "+Mm(walls.Average())+" mm";
            return text;
        }
    }
}
