using System;
using System.Linq;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.Geometry
{
    public sealed class RayHit
    {
        public int RayIndex { get; set; }
        public Vector3 Point { get; set; }
        public double Distance { get; set; }
        public IFace2 Face { get; set; }
    }

    public sealed class TerminationAnalysis
    {
        public TerminationType Type { get; set; } = TerminationType.Ambiguous;
        public bool TopologyOpenAtMin { get; set; }
        public bool TopologyOpenAtMax { get; set; }
        public RayHit NegativeHit { get; set; }
        public RayHit PositiveHit { get; set; }
        public string Evidence { get; set; } = string.Empty;
    }

    public sealed class HoleTerminationAnalyzer
    {
        private readonly FaceAnalyzer _faces;
        public HoleTerminationAnalyzer(FaceAnalyzer faces)
        {
            _faces = faces ?? throw new ArgumentNullException(nameof(faces));
        }

        public TerminationAnalysis Analyze(IModelDoc2 model, HoleGroup group)
        {
            var result = new TerminationAnalysis();
            if (model == null || group?.Body == null)
            {
                result.Evidence = "文档或 Body 无效。";
                return result;
            }
            try
            {
                double tolerance = Math.Max(1e-6, group.MainRadius * 1e-3);
                Logger.Info("终止判断：使用端面拓扑，不调用原生射线求交。");
                result.TopologyOpenAtMin = IsTopologyOpen(group, group.MinStation, tolerance);
                result.TopologyOpenAtMax = IsTopologyOpen(group, group.MaxStation, tolerance);
                if (result.TopologyOpenAtMin && result.TopologyOpenAtMax)
                    result.Type = TerminationType.Through;
                else if (result.TopologyOpenAtMin != result.TopologyOpenAtMax)
                {
                    bool bottomAtMax = result.TopologyOpenAtMin;
                    double station = bottomAtMax ? group.MaxStation : group.MinStation;
                    var point = SwMath.PointAt(group.AxisOrigin, group.Axis, station);
                    var bottom = group.Boundaries.Where(b => Math.Abs(b.Station-station)<=tolerance)
                        .Select(b => b.BoundaryFace).FirstOrDefault(f => _faces.TryGetPlane(f,out var plane)
                            && Math.Abs(Vector3.Dot(plane.Normal, group.Axis)) > 0.999
                            && _faces.FaceCoversAxisAt(f,point,tolerance));
                    if (bottom == null)
                    {
                        foreach (var cone in group.Cones)
                        {
                            if (cone.CircularEdges.Count == 0 || cone.CircularEdges.Any(e =>
                                Math.Abs(SwMath.ProjectStation(e.Center,group.AxisOrigin,group.Axis)-station)>tolerance)) continue;
                            double tip = station + (bottomAtMax ? 1 : -1) * cone.CircularEdges[0].Radius / Math.Tan(cone.Geometry.HalfAngleRadians);
                            var apex = SwMath.PointAt(group.AxisOrigin,group.Axis,tip);
                            if (!_faces.FaceCoversAxisAt(cone.Face,apex,tolerance)) continue;
                            point = apex;
                            bottom = cone.Face;
                            break;
                        }
                    }
                    if (bottom != null)
                    {
                        result.Type = TerminationType.Blind;
                        var hit = new RayHit { Point=point, Face=bottom, Distance=Math.Abs(SwMath.ProjectStation(point,group.AxisOrigin,group.Axis)-(group.MinStation+group.MaxStation)/2) };
                        if(bottomAtMax) result.PositiveHit=hit; else result.NegativeHit=hit;
                    }
                }
                result.Evidence = $"Topology(min={result.TopologyOpenAtMin}, max={result.TopologyOpenAtMax}); " +
                    "按所属实体的孔口与孔底判断。";
                Logger.Info("终止判断：" + result.Type + "；" + result.Evidence);
            }
            catch (Exception ex)
            {
                result.Type = TerminationType.Ambiguous;
                result.Evidence = "终止判断异常：" + ex.Message;
                Logger.Error(result.Evidence, ex);
            }
            return result;
        }

        private bool IsTopologyOpen(HoleGroup group, double station, double tolerance)
        {
            var connections = group.Boundaries.Where(b => Math.Abs(b.Station - station) <= tolerance).ToList();
            if (connections.Count == 0) return false;
            Vector3 axisPoint = SwMath.PointAt(group.AxisOrigin, group.Axis, station);
            return connections.All(c => !_faces.FaceCoversAxisAt(c.BoundaryFace, axisPoint, tolerance));
        }
    }
}
