using System;
using System.Collections.Generic;
using System.Linq;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Geometry;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Inspection
{
    public sealed class HoleInspectionReport
    {
        public int Total { get; set; }
        public int Marked { get; set; }
        public int Unmarked => Math.Max(0, Total - Marked);
        public int Ambiguous { get; set; }

        public override string ToString() => $"总孔数={Total}，已标记={Marked}，未标记={Unmarked}，Ambiguous={Ambiguous}";
    }

    public sealed class HoleInspector
    {
        private readonly HoleDetector _detector;
        private readonly HoleAttributeManager _attributes;

        public HoleInspector(HoleDetector detector, HoleAttributeManager attributes)
        {
            _detector = detector;
            _attributes = attributes;
        }

        public HoleInspectionReport Scan(IModelDoc2 model)
        {
            var report = new HoleInspectionReport();
            if (!(model is IPartDoc part))
            {
                Logger.Warn("孔检查器：当前文档不是零件。" );
                return report;
            }

            var visited = new HashSet<long>();
            try
            {
                foreach (IBody2 body in SwMath.ToComList<IBody2>(part.GetBodies2((int)swBodyType_e.swSolidBody, false)))
                {
                    foreach (IFace2 face in SwMath.ToComList<IFace2>(body.GetFaces()))
                    {
                        long id = SwMath.GetComIdentity(face);
                        if (visited.Contains(id) || !IsPotentialInnerCylinder(face))
                        {
                            continue;
                        }

                        OperationResult<HoleAnalysis> analysis = _detector.AnalyzeFace(model, face);
                        if (!analysis.Success)
                        {
                            continue;
                        }

                        report.Total++;
                        foreach (CylindricalFaceInfo cylinder in analysis.Value.Group.Cylinders)
                        {
                            visited.Add(SwMath.GetComIdentity(cylinder.Face));
                        }

                        if (analysis.Value.Group.Cylinders.Any(cylinder => _attributes.Read(cylinder.Face) != null))
                        {
                            report.Marked++;
                        }
                        if (analysis.Value.Definition.TerminationType == TerminationType.Ambiguous)
                        {
                            report.Ambiguous++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("孔检查器扫描失败。", ex);
            }

            Logger.Info("孔检查器：" + report);
            return report;
        }

        private static bool IsPotentialInnerCylinder(IFace2 face)
        {
            try
            {
                var surface = face?.GetSurface() as ISurface;
                return surface != null && surface.IsCylinder() && face.FaceInSurfaceSense();
            }
            catch
            {
                return false;
            }
        }
    }
}
