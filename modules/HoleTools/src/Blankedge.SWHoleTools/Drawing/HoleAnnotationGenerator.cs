using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Drawing
{
    public sealed class HoleAnnotationGenerator
    {
        public string Generate(HoleDefinition hole)
        {
            if (hole == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(hole.AnnotationText))
            {
                return ExplicitThreadText(hole, hole.AnnotationText);
            }

            string firstLine;
            if (hole.HoleType == HoleSemanticType.Threaded && !string.IsNullOrWhiteSpace(hole.ThreadSize))
            {
                string pitch = hole.Pitch > 0.0 ? "×" + Millimeters(hole.Pitch) : string.Empty;
                string tolerance = string.IsNullOrWhiteSpace(hole.Tolerance) ? string.Empty : "-" + hole.Tolerance;
                firstLine = hole.ThreadSize + pitch + tolerance;
            }
            else
            {
                firstLine = "Ø" + Millimeters(hole.Diameter);
            }

            string secondLine;
            if (hole.TerminationType == TerminationType.Through || hole.TerminationType == TerminationType.InterBody)
            {
                secondLine = hole.HoleType == HoleSemanticType.Threaded ? "螺纹通孔" : "通孔";
            }
            else if (hole.MeasuredDepth > 0.0)
            {
                secondLine = hole.HoleType == HoleSemanticType.Threaded
                    ? "螺纹盲孔"
                    : "DEPTH " + Millimeters(hole.MeasuredDepth);
            }
            else
            {
                secondLine = hole.TerminationType.ToString().ToUpperInvariant();
            }

            return ExplicitThreadText(hole, firstLine + System.Environment.NewLine + secondLine);
        }

        public bool CreateSimpleNote(IModelDoc2 drawingModel, DrawingHoleMatch match)
        {
            if (drawingModel == null || match?.Hole == null || match.View == null)
            {
                return false;
            }

            try
            {
                // First-stage fallback annotation. Native Hole Callout and leader attachment remain extension points.
                var note = drawingModel.InsertNote(Generate(match.Hole)) as INote;
                if (note == null)
                {
                    return false;
                }

                double[] outline = SwMath.ToDoubleArray(match.View.GetOutline());
                var annotation = note.GetAnnotation() as IAnnotation;
                if (annotation != null && outline.Length >= 4)
                {
                    annotation.SetPosition2(outline[2] + 0.01, outline[3], 0.0);
                }

                Logger.Info("已创建基础工程图 Note 示例：" + match.Hole.HoleID);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("创建工程图 Note 失败。", ex);
                return false;
            }
        }

        public int CreateNotes(IModelDoc2 drawingModel, IEnumerable<DrawingHoleMatch> matches)
        {
            if (drawingModel == null || matches == null)
            {
                return 0;
            }

            int created = 0;
            var offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var usedCenters = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidates in matches
                .Where(item => item?.Hole != null && item.View != null)
                .GroupBy(item => item.Hole.HoleID ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => ProjectedCenter(group.OrderByDescending(AxisAlignment).First())[1]))
            {
                try
                {
                    DrawingHoleMatch match = null;
                    foreach (var candidate in candidates.OrderByDescending(AxisAlignment))
                    {
                        var center = ProjectedCenter(candidate);
                        if (usedCenters.TryGetValue(candidate.View.Name, out var prior) && prior.Any(p =>
                            Math.Abs(p[0] - center[0]) < 0.001 && Math.Abs(p[1] - center[1]) < 0.001)) continue;
                        drawingModel.ClearSelection2(true);
                        if (SelectHoleEdge(candidate)) { match = candidate; break; }
                    }
                    // Never emit an unattached note that looks like a verified hole callout.
                    if (match == null) { Logger.Info("未找到可附着孔边线：" + candidates.Key); continue; }
                    var note = drawingModel.InsertNote(Generate(match.Hole)) as INote;
                    var annotation = note?.GetAnnotation() as IAnnotation;
                    double[] outline = SwMath.ToDoubleArray(match.View.GetOutline());
                    if (annotation == null || outline.Length < 4)
                    {
                        continue;
                    }

                    bool left = ProjectedCenter(match)[0] < (outline[0] + outline[2]) * 0.5 - 0.001;
                    string viewKey = (match.View.Name ?? string.Empty) + (left ? ":L" : ":R");
                    offsets.TryGetValue(viewKey, out int index);
                    offsets[viewKey] = index + 1;
                    double x = left ? Math.Max(0.035, outline[0] - 0.050) : outline[2] + 0.008;
                    double y = outline[3] - (index * 0.012);
                    var sheet = ((IDrawingDoc)drawingModel).GetCurrentSheet() as ISheet;
                    if (sheet != null)
                    {
                        double sheetWidth=0, sheetHeight=0; sheet.GetSize(ref sheetWidth,ref sheetHeight);
                        // Keep text clear of the left binding strip, right frame and bottom title area.
                        double textWidth=Math.Max(.045,Generate(match.Hole).Split('\n').Max(line=>line.Sum(c=>c>255?.0035:.002)));
                        x=Math.Max(.035,Math.Min(x,sheetWidth-.015-textWidth));
                        y=Math.Max(.085,Math.Min(y,sheetHeight-.025));
                    }
                    annotation.SetLeader3((int)swLeaderStyle_e.swBENT, (int)swLeaderSide_e.swLS_SMART, true, false, false, false);
                    annotation.SetPosition2(x, y, 0.0);
                    if (!usedCenters.TryGetValue(match.View.Name, out var centers))
                        usedCenters[match.View.Name] = centers = new List<double[]>();
                    centers.Add(ProjectedCenter(match));
                    created++;
                }
                catch (Exception ex)
                {
                    Logger.Error("批量创建工程图孔 Note 失败，继续处理。", ex);
                }
                finally { drawingModel.ClearSelection2(true); }
            }

            Logger.Info("工程图孔 Note 创建数量：" + created);
            return created;
        }

        public static string ExplicitThreadText(HoleDefinition hole, string text)
        {
            if(hole?.HoleType != HoleSemanticType.Threaded || string.IsNullOrWhiteSpace(text))return text;
            // Labels describe a reusable thread specification. They must not override
            // the geometric end condition of the individual hole receiving the label.
            string ending = hole.TerminationType == TerminationType.Blind ? "螺纹盲孔" :
                (hole.TerminationType == TerminationType.Through || hole.TerminationType == TerminationType.InterBody)
                    ? "螺纹通孔" : "螺纹孔（通盲待复核）";
            text=System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<![非不纹])(?:螺纹)?(?:通孔|盲孔)|\b(?:THRU|THROUGH|BLIND)\b", ending,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (hole.TerminationType == TerminationType.Blind)
            {
                if (hole.MeasuredDepth > 0 && !System.Text.RegularExpressions.Regex.IsMatch(text,
                    @"底孔深|钻孔深|孔深|\bDEPTH\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    text += System.Environment.NewLine + "底孔深 " + Millimeters(hole.MeasuredDepth) + " mm";
                if (hole.ThreadDepth > 0 && !System.Text.RegularExpressions.Regex.IsMatch(text,
                    @"螺纹深|攻牙深|攻丝深|THREAD\s*DEPTH", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    text += System.Environment.NewLine + "螺纹深 " + Millimeters(hole.ThreadDepth) + " mm";
            }
            return text;
        }
        private static string Millimeters(double meters)
        {
            return (meters * 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static double AxisAlignment(DrawingHoleMatch match)
        {
            var transform = match.View.ModelToViewTransform as IMathTransform;
            var a = transform?.ArrayData as double[];
            if (a == null || a.Length < 9) return 0;
            return Math.Abs(match.Hole.Axis.X * a[2] + match.Hole.Axis.Y * a[5] + match.Hole.Axis.Z * a[8]);
        }

        private static double[] ProjectedCenter(DrawingHoleMatch match)
        {
            var a = ((IMathTransform)match.View.ModelToViewTransform).ArrayData as double[];
            var p = match.Hole.Center;
            return new[] { (p.X * a[0] + p.Y * a[3] + p.Z * a[6]) * a[12] + a[9],
                (p.X * a[1] + p.Y * a[4] + p.Z * a[7]) * a[12] + a[10] };
        }

        private static bool SelectHoleEdge(DrawingHoleMatch match)
        {
            var edges = match.ModelFace?.GetEdges() as object[];
            if (edges == null) return false;
            foreach (IEdge edge in edges)
            {
                var curve = edge.GetCurve() as ICurve;
                if (curve == null || !curve.IsCircle()) continue;
                if (match.View.SelectEntity((IEntity)edge, false)) return true;
            }
            return false;
        }
    }
}
