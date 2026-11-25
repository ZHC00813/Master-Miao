using System;
using System.Linq;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Geometry
{
    public sealed class HoleDetector
    {
        private readonly FaceAnalyzer _faceAnalyzer;
        private readonly HoleGroupResolver _groupResolver;
        private readonly HoleTerminationAnalyzer _terminationAnalyzer;
        private readonly HoleDepthCalculator _depthCalculator;
        private readonly PersistentReferenceManager _persistentReferences;

        public HoleDetector(PersistentReferenceManager persistentReferences)
        {
            _persistentReferences = persistentReferences ?? throw new ArgumentNullException(nameof(persistentReferences));
            _faceAnalyzer = new FaceAnalyzer();
            _groupResolver = new HoleGroupResolver(_faceAnalyzer);
            _terminationAnalyzer = new HoleTerminationAnalyzer(_faceAnalyzer);
            _depthCalculator = new HoleDepthCalculator();
        }

        public OperationResult<HoleAnalysis> AnalyzeSelection(IModelDoc2 model)
        {
            if (model == null)
            {
                return OperationResult<HoleAnalysis>.Fail("当前没有打开文档。" );
            }

            try
            {
                if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    return OperationResult<HoleAnalysis>.Fail("孔识别仅支持当前活动的 .SLDPRT 零件文档。" );
                }

                var selection = model.SelectionManager as ISelectionMgr;
                object selected = selection?.GetSelectedObject6(1, -1);
                var face = selected as IFace2;
                if (face == null)
                {
                    return OperationResult<HoleAnalysis>.Fail("请先选择一个孔的圆柱内壁。" );
                }

                try
                {
                    if (!face.FaceInSurfaceSense())
                    {
                        return OperationResult<HoleAnalysis>.Fail("所选圆柱面看起来是凸出的外圆柱面，而不是孔内壁。" );
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("检查圆柱面的内/外法向失败，将继续做几何分析。", ex);
                }

                Logger.Info("已读取用户选择的 Face。" );
                return AnalyzeFace(model, face);
            }
            catch (Exception ex)
            {
                Logger.Error("读取当前选择失败。", ex);
                return OperationResult<HoleAnalysis>.Fail("读取选择失败：" + ex.Message);
            }
        }

        public OperationResult<HoleAnalysis> AnalyzeFace(IModelDoc2 model, IFace2 face)
        {
            try
            {
                if (!_faceAnalyzer.TryGetCylinder(face, out CylinderGeometry cylinder, out string error))
                {
                    return OperationResult<HoleAnalysis>.Fail(error);
                }

                if (!face.FaceInSurfaceSense()) return OperationResult<HoleAnalysis>.Fail("所选面是外圆柱面，不是孔内壁。");
                Logger.Info($"识别圆柱：半径={cylinder.Radius:F9} m，轴线={cylinder.Axis}。" );
                OperationResult<HoleGroup> groupResult = _groupResolver.Resolve(face, cylinder);
                if (!groupResult.Success)
                {
                    return OperationResult<HoleAnalysis>.Fail(groupResult.Error);
                }

                HoleGroup group = groupResult.Value;
                // A concave cylindrical surface may be an open slot, not a closed bore.
                // Imported bores often have two or more cylindrical patches.
                // Validate their shared seams as well as the angular wrap.
                bool closedWall = HasClosedWall(group);
                if (!closedWall)
                    return OperationResult<HoleAnalysis>.Fail("圆柱面未形成可确认的完整孔壁，可能为开口槽或分割面，请人工复核。");
                TerminationAnalysis termination = _terminationAnalyzer.Analyze(model, group);
                double middle = (group.MinStation + group.MaxStation) * 0.5;
                var definition = new HoleDefinition
                {
                    BodyID = CreateBodyId(model, group.Body),
                    Diameter = group.MainRadius * 2.0,
                    Axis = group.Axis,
                    Center = SwMath.PointAt(group.AxisOrigin, group.Axis, middle),
                    FacePersistentReference = _persistentReferences.Create(model, group.PrimaryCylinderFace)
                };

                _depthCalculator.Populate(definition, group, termination);
                return OperationResult<HoleAnalysis>.Ok(new HoleAnalysis
                {
                    Definition = definition,
                    Group = group,
                    Evidence = termination.Evidence
                });
            }
            catch (Exception ex)
            {
                Logger.Error("孔识别失败。", ex);
                return OperationResult<HoleAnalysis>.Fail("孔识别失败：" + ex.Message);
            }
        }

        private bool HasClosedWall(HoleGroup group)
        {
            const double angleTolerance = 1e-6;
            foreach (var seed in group.Cylinders)
            {
                var uv = SwMath.ToDoubleArray(seed.Face.GetUVBounds());
                if (uv.Length >= 2 && Math.Abs(uv[1] - uv[0]) >= 2 * Math.PI - angleTolerance)
                    return true;

                double tolerance = Math.Max(1e-7, seed.Geometry.Radius * 1e-4);
                double station = (seed.MinStation + seed.MaxStation) * 0.5;
                var patches = group.Cylinders.Where(c =>
                    Math.Abs(c.Geometry.Radius - seed.Geometry.Radius) <= tolerance &&
                    c.MinStation < station - tolerance && c.MaxStation > station + tolerance).ToList();
                if (patches.Count < 2) continue;

                double angle = 0;
                bool closed = true;
                foreach (var patch in patches)
                {
                    var bounds = SwMath.ToDoubleArray(patch.Face.GetUVBounds());
                    if (bounds.Length < 2) { closed = false; break; }
                    angle += Math.Abs(bounds[1] - bounds[0]);
                    foreach (IEdge edge in SwMath.ToComList<IEdge>(patch.Face.GetEdges()))
                    {
                        var curve = edge.GetCurve() as ICurve;
                        if (curve != null && curve.IsCircle()) continue; // axial end boundary
                        // Every longitudinal seam must join another patch of this
                        // same bore. A slot instead borders a plane or an open edge.
                        long ownId = SwMath.GetComIdentity(patch.Face);
                        bool joined = SwMath.ToComList<IFace2>(edge.GetTwoAdjacentFaces2()).Any(adjacent =>
                            adjacent != null && SwMath.GetComIdentity(adjacent) != ownId &&
                            patches.Any(p => SwMath.GetComIdentity(p.Face) == SwMath.GetComIdentity(adjacent)));
                        if (!joined) { closed = false; break; }
                    }
                    if (!closed) break;
                }
                if (closed && Math.Abs(angle - 2 * Math.PI) <= angleTolerance * patches.Count)
                    return true;
            }
            return false;
        }

        private string CreateBodyId(IModelDoc2 model, IBody2 body)
        {
            string persistent = _persistentReferences.Create(model, body);
            if (!string.IsNullOrWhiteSpace(persistent))
            {
                return persistent;
            }

            try
            {
                return string.IsNullOrWhiteSpace(body?.Name) ? "Body-Unresolved" : body.Name;
            }
            catch
            {
                return "Body-Unresolved";
            }
        }
    }
}
