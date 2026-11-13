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

                Logger.Info($"识别圆柱：半径={cylinder.Radius:F9} m，轴线={cylinder.Axis}。" );
                OperationResult<HoleGroup> groupResult = _groupResolver.Resolve(face, cylinder);
                if (!groupResult.Success)
                {
                    return OperationResult<HoleAnalysis>.Fail(groupResult.Error);
                }

                HoleGroup group = groupResult.Value;
                // A concave cylindrical surface may be an open slot, not a closed bore.
                // Split cylindrical walls without a complete wrap require manual review.
                bool closedWall = group.Cylinders.Any(item =>
                {
                    var uv = item.Face.GetUVBounds() as double[];
                    return uv != null && uv.Length >= 2 && Math.Abs(uv[1] - uv[0]) >= 2.0 * Math.PI - 1e-6;
                });
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
