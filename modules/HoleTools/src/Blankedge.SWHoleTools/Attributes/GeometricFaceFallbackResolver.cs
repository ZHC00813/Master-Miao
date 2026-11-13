using System;
using Blankedge.SWHoleTools.Geometry;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Attributes
{
    public sealed class GeometricFaceFallbackResolver : IPersistentReferenceFallbackResolver
    {
        private readonly FaceAnalyzer _analyzer = new FaceAnalyzer();

        public IFace2 TryResolveFace(IModelDoc2 model, HoleDefinition definition)
        {
            if (!(model is IPartDoc part) || definition == null)
            {
                return null;
            }

            try
            {
                IFace2 best = null;
                double bestScore = double.PositiveInfinity;
                foreach (IBody2 body in SwMath.ToComList<IBody2>(part.GetBodies2((int)swBodyType_e.swSolidBody, false)))
                {
                    foreach (IFace2 face in SwMath.ToComList<IFace2>(body.GetFaces()))
                    {
                        if (!_analyzer.TryGetCylinder(face, out CylinderGeometry cylinder, out _))
                        {
                            continue;
                        }

                        double diameterError = Math.Abs((cylinder.Radius * 2.0) - definition.Diameter);
                        if (diameterError > Math.Max(1e-5, definition.Diameter * 1e-3) ||
                            !SwMath.AreParallel(cylinder.Axis, definition.Axis))
                        {
                            continue;
                        }

                        double axisError = SwMath.DistancePointToLine(definition.Center, cylinder.Origin, cylinder.Axis);
                        double score = diameterError + axisError;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = face;
                        }
                    }
                }

                if (best != null)
                {
                    Logger.Warn("Persistent Reference 失效，已使用几何参数回退匹配 Face。" );
                }
                return best;
            }
            catch (Exception ex)
            {
                Logger.Error("几何回退匹配失败。", ex);
                return null;
            }
        }
    }
}
