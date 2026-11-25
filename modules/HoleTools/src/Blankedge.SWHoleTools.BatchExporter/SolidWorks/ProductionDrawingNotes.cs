using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Blankedge.SWHoleTools.BatchExporter.Model;
using Blankedge.SWHoleTools.Utilities;
using Blankedge.SWHoleTools.Drawing;
namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    public static class ProductionDrawingNotes
    {
        public static IList<DrawingHoleMatch> GroupIdenticalLabels(IList<DrawingHoleMatch> matches)
        {
            var result = new List<DrawingHoleMatch>(matches.Where(m => m.Hole.HoleType == Blankedge.SWHoleTools.Model.HoleSemanticType.Threaded));
            var generator = new HoleAnnotationGenerator();
            foreach (var group in matches.Where(m => m.Hole.HoleType != Blankedge.SWHoleTools.Model.HoleSemanticType.Threaded).GroupBy(m => (string.IsNullOrEmpty(m.Hole.TagId) ? m.Hole.HoleID : m.Hole.TagId) + "|" + generator.Generate(m.Hole)))
            {
                var first = group.First();
                int count = group.Select(m => m.Hole.HoleID).Distinct().Count();
                string text = (count > 1 ? count + "-" : "") + generator.Generate(first.Hole);
                foreach (var match in group.Where(m => m.Hole.HoleID == first.Hole.HoleID))
                { match.Hole.AnnotationText = text; result.Add(match); }
            }
            return result;
        }

        private sealed class Point { public IEntity Entity; public double X, Y; }
        public static int AddDimensions(IModelDoc2 drawing, IView view)
        {
            int count = 0;
            try
            {
                var t = (double[])view.ModelToViewTransform.ArrayData;
                var points = new List<Point>();
                foreach (IComponent2 component in SwMath.ToComList<IComponent2>(view.GetVisibleComponents()))
                    foreach (IVertex vertex in SwMath.ToComList<IVertex>(view.GetVisibleEntities2((Component2)component, (int)swViewEntityType_e.swViewEntityType_Vertex)))
                    {
                        var p = (double[])vertex.GetPoint();
                        points.Add(new Point { Entity = (IEntity)vertex, X = (p[0]*t[0]+p[1]*t[3]+p[2]*t[6])*t[12]+t[9], Y = (p[0]*t[1]+p[1]*t[4]+p[2]*t[7])*t[12]+t[10] });
                    }
                if (points.Count < 2) return AddRoundDimensions(drawing, view);
                var box = (double[])view.GetOutline();
                var left = points.OrderBy(p=>p.X).First(); var right = points.OrderByDescending(p=>p.X).First();
                var bottom = points.OrderBy(p=>p.Y).First(); var top = points.OrderByDescending(p=>p.Y).First();
                drawing.ClearSelection2(true);
                if (right.X-left.X > 1e-5 && view.SelectEntity(left.Entity,false) && view.SelectEntity(right.Entity,true))
                    if (drawing.AddHorizontalDimension2((box[0]+box[2])/2,box[1]-.012,0) != null) count++;
                drawing.ClearSelection2(true);
                if (top.Y-bottom.Y > 1e-5 && view.SelectEntity(bottom.Entity,false) && view.SelectEntity(top.Entity,true))
                    if (drawing.AddVerticalDimension2(box[0]-.012,(box[1]+box[3])/2,0) != null) count++;
            }
            catch (Exception ex) { Logger.Error("生成参考尺寸失败，保留视图供复核。",ex); }
            finally { drawing.ClearSelection2(true); }
            return count;
        }
        private static int AddRoundDimensions(IModelDoc2 drawing, IView view)
        {
            var t = (double[])view.ModelToViewTransform.ArrayData;
            var circles = new List<Tuple<IEntity,double[],double,double,double>>();
            foreach (var component in SwMath.ToComList<IComponent2>(view.GetVisibleComponents()))
                foreach (var edge in SwMath.ToComList<IEdge>(view.GetVisibleEntities2((Component2)component,(int)swViewEntityType_e.swViewEntityType_Edge)))
                {
                    var curve = edge.GetCurve() as ICurve;
                    if (curve == null || !curve.IsCircle()) continue;
                    var p = (double[])curve.CircleParams;
                    double x=(p[0]*t[0]+p[1]*t[3]+p[2]*t[6])*t[12]+t[9];
                    double y=(p[0]*t[1]+p[1]*t[4]+p[2]*t[7])*t[12]+t[10];
                    double alignment=Math.Abs(p[3]*t[2]+p[4]*t[5]+p[5]*t[8]);
                    circles.Add(Tuple.Create((IEntity)edge,p,x,y,alignment));
                }
            if (circles.Count == 0) return 0;
            var box=(double[])view.GetOutline(); int count=0;
            var first=circles[0]; var last=first; double distance=0;
            foreach(var a in circles) foreach(var b in circles)
            {
                double d=(a.Item3-b.Item3)*(a.Item3-b.Item3)+(a.Item4-b.Item4)*(a.Item4-b.Item4);
                if(d>distance){distance=d;first=a;last=b;}
            }
            drawing.ClearSelection2(true);
            if(distance>1e-10 && view.SelectEntity(first.Item1,false) && view.SelectEntity(last.Item1,true))
            {
                object dimension=Math.Abs(first.Item3-last.Item3)>=Math.Abs(first.Item4-last.Item4)
                    ? drawing.AddHorizontalDimension2((box[0]+box[2])/2,box[1]-.012,0)
                    : drawing.AddVerticalDimension2(box[0]-.012,(box[1]+box[3])/2,0);
                if(dimension!=null)count++;
            }
            var end=circles.Where(c=>c.Item5>.999).OrderByDescending(c=>c.Item2[6]).FirstOrDefault();
            drawing.ClearSelection2(true);
            if(end!=null && view.SelectEntity(end.Item1,false))
                if(drawing.AddDiameterDimension2(box[2]+.012,box[3]+.012,0)!=null)count++;
            drawing.ClearSelection2(true);
            return count;
        }
        public static void AddSummary(IModelDoc2 drawing, BatchExportOptions options, string source, DrawingLayoutPlan plan, int annotations, string measurements = "")
        {
            var sheetView = ((IDrawingDoc)drawing).GetFirstView() as IView;
            if (sheetView != null)
                foreach (INote templateNote in SwMath.ToComList<INote>(sheetView.GetNotes()))
                {
                    string placeholder = (templateNote.GetText() ?? string.Empty).Trim().Trim('"', '“', '”');
                    if (placeholder == "图样名称" || placeholder == "图样代号")
                        if (!templateNote.SetText(options.OutputName(source)))
                            throw new InvalidOperationException("标题栏填写失败：" + placeholder);
                }
            drawing.ClearSelection2(true);
            var note = drawing.InsertNote(BuildSummaryText(options,source,plan,annotations,measurements)) as INote;
            var annotation = note?.GetAnnotation() as IAnnotation;
            if (annotation == null) throw new InvalidOperationException("生产说明写入失败。");
            annotation.SetPosition2(.028,.060,0);
        }
        public static string BuildSummaryText(BatchExportOptions options,string source,DrawingLayoutPlan plan,int annotations,string measurements)
        {
            var lines = new List<string> { options.OutputName(source) + " / " + options.Category, "单位 mm" };
            if (!string.IsNullOrWhiteSpace(measurements)) lines.Add(measurements);
            if (!string.IsNullOrWhiteSpace(options.MaterialText)) lines.Add("材料：" + options.MaterialText);
            if (!string.IsNullOrWhiteSpace(options.FinishText)) lines.Add("表面处理：" + options.FinishText);
            if (!string.IsNullOrWhiteSpace(options.ManufacturingNotes)) lines.Add(options.ManufacturingNotes);
            if (options.Category == ProductionCategory.焊接) lines.Add("焊接组成及数量见明细表；焊缝要求和装配位置需复核。");
            else lines.Add("孔注释 " + annotations + " 项；自动尺寸为视图顶点参考尺寸，生产前请复核。");
            // A4 has a narrow notes area beside the title block; wrap by approximate glyph width.
            int width = plan.Paper.Width <= .30 ? 20 : 56;
            var wrapped = new List<string>();
            foreach (string line in lines)
            {
                string segment = ""; double used = 0;
                foreach (char c in line)
                {
                    double unit = c < 128 ? .55 : 1;
                    if (used + unit > width) { wrapped.Add(segment); segment = ""; used = 0; }
                    segment += c; used += unit;
                }
                if (segment.Length > 0) wrapped.Add(segment);
            }
            return string.Join("\n",wrapped);
        }
    }
}
