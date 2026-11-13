using System;
using System.Collections.Generic;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.Geometry
{
    public sealed class CylinderGeometry
    {
        public Vector3 Origin { get; set; }
        public Vector3 Axis { get; set; }
        public double Radius { get; set; }
    }

    public sealed class ConeGeometry
    {
        public Vector3 Origin { get; set; }
        public Vector3 Axis { get; set; }
        public double RadiusAtOrigin { get; set; }
        public double HalfAngleRadians { get; set; }
    }

    public sealed class PlaneGeometry
    {
        public Vector3 Origin { get; set; }
        public Vector3 Normal { get; set; }
    }

    public sealed class CircularEdgeInfo
    {
        public IEdge Edge { get; set; }
        public Vector3 Center { get; set; }
        public Vector3 Axis { get; set; }
        public double Radius { get; set; }
    }

    public sealed class CylindricalFaceInfo
    {
        public IFace2 Face { get; set; }
        public CylinderGeometry Geometry { get; set; }
        public double MinStation { get; set; }
        public double MaxStation { get; set; }
        public IList<CircularEdgeInfo> CircularEdges { get; } = new List<CircularEdgeInfo>();
    }

    public sealed class ConicalFaceInfo
    {
        public IFace2 Face { get; set; }
        public ConeGeometry Geometry { get; set; }
        public double MinStation { get; set; }
        public double MaxStation { get; set; }
        public double MajorRadius { get; set; }
        public IList<CircularEdgeInfo> CircularEdges { get; } = new List<CircularEdgeInfo>();
    }

    public sealed class FaceAnalyzer
    {
        public bool TryGetCylinder(IFace2 face, out CylinderGeometry geometry, out string error)
        {
            geometry = null;
            error = string.Empty;
            if (face == null)
            {
                error = "Face 为空。";
                return false;
            }

            try
            {
                var surface = face.GetSurface() as ISurface;
                if (surface == null || !surface.IsCylinder())
                {
                    error = "所选面不是圆柱面。";
                    return false;
                }

                double[] data = SwMath.ToDoubleArray(surface.CylinderParams);
                if (data.Length < 7)
                {
                    error = "SOLIDWORKS 未返回完整的圆柱参数。";
                    return false;
                }

                Vector3 axis = new Vector3(data[3], data[4], data[5]).Normalized();
                if (axis.Length <= SwMath.GeometryTolerance || data[6] <= SwMath.GeometryTolerance)
                {
                    error = "圆柱轴线或半径无效。";
                    return false;
                }

                geometry = new CylinderGeometry
                {
                    Origin = new Vector3(data[0], data[1], data[2]),
                    Axis = axis,
                    Radius = Math.Abs(data[6])
                };
                return true;
            }
            catch (Exception ex)
            {
                error = "读取圆柱面失败：" + ex.Message;
                Logger.Error(error, ex);
                return false;
            }
        }

        public bool TryGetCone(IFace2 face, out ConeGeometry geometry)
        {
            geometry = null;
            if (face == null)
            {
                return false;
            }

            try
            {
                var surface = face.GetSurface() as ISurface;
                if (surface == null || !surface.IsCone())
                {
                    return false;
                }

                double[] data;
                try
                {
                    data = SwMath.ToDoubleArray(surface.ConeParams2);
                }
                catch
                {
                    data = SwMath.ToDoubleArray(surface.ConeParams);
                }

                if (data.Length < 8)
                {
                    return false;
                }

                Vector3 axis = new Vector3(data[3], data[4], data[5]).Normalized();
                if (axis.Length <= SwMath.GeometryTolerance)
                {
                    return false;
                }

                geometry = new ConeGeometry
                {
                    Origin = new Vector3(data[0], data[1], data[2]),
                    Axis = axis,
                    RadiusAtOrigin = Math.Abs(data[6]),
                    HalfAngleRadians = Math.Abs(data[7])
                };
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("读取锥面失败。", ex);
                return false;
            }
        }

        public bool TryGetPlane(IFace2 face, out PlaneGeometry geometry)
        {
            geometry = null;
            try
            {
                var surface = face?.GetSurface() as ISurface;
                if (surface == null || !surface.IsPlane())
                {
                    return false;
                }

                double[] data = SwMath.ToDoubleArray(surface.PlaneParams);
                if (data.Length < 6)
                {
                    return false;
                }

                geometry = new PlaneGeometry
                {
                    Origin = new Vector3(data[3], data[4], data[5]),
                    Normal = new Vector3(data[0], data[1], data[2]).Normalized()
                };
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("读取平面参数失败。", ex);
                return false;
            }
        }

        public IList<CircularEdgeInfo> GetCircularEdges(IFace2 face)
        {
            var result = new List<CircularEdgeInfo>();
            try
            {
                foreach (IEdge edge in SwMath.ToComList<IEdge>(face?.GetEdges()))
                {
                    try
                    {
                        var curve = edge.GetCurve() as ICurve;
                        if (curve == null || !curve.IsCircle())
                        {
                            continue;
                        }

                        double[] data = SwMath.ToDoubleArray(curve.CircleParams);
                        if (data.Length >= 7)
                        {
                            result.Add(new CircularEdgeInfo
                            {
                                Edge = edge,
                                Center = new Vector3(data[0], data[1], data[2]),
                                Axis = new Vector3(data[3], data[4], data[5]).Normalized(),
                                Radius = Math.Abs(data[6])
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("读取圆边参数失败，已跳过该边。", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("遍历 Face 边失败。", ex);
            }

            return result;
        }

        public bool TryGetAxialExtent(IFace2 face, Vector3 origin, Vector3 axis, out double min, out double max)
        {
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;

            foreach (CircularEdgeInfo circle in GetCircularEdges(face))
            {
                double station = SwMath.ProjectStation(circle.Center, origin, axis);
                min = Math.Min(min, station);
                max = Math.Max(max, station);
            }

            try
            {
                foreach (IEdge edge in SwMath.ToComList<IEdge>(face?.GetEdges()))
                {
                    AddVertexStation(edge.GetStartVertex() as IVertex, origin, axis, ref min, ref max);
                    AddVertexStation(edge.GetEndVertex() as IVertex, origin, axis, ref min, ref max);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取 Face 顶点范围失败。", ex);
            }

            if (double.IsInfinity(min) || double.IsInfinity(max))
            {
                return false;
            }

            return true;
        }

        public bool AreCoaxial(CylinderGeometry first, CylinderGeometry second, double linearTolerance)
        {
            return first != null && second != null &&
                   SwMath.AreParallel(first.Axis, second.Axis) &&
                   SwMath.DistancePointToLine(second.Origin, first.Origin, first.Axis) <= linearTolerance;
        }

        public bool IsCoaxial(CylinderGeometry cylinder, ConeGeometry cone, double linearTolerance)
        {
            return cylinder != null && cone != null &&
                   SwMath.AreParallel(cylinder.Axis, cone.Axis) &&
                   SwMath.DistancePointToLine(cone.Origin, cylinder.Origin, cylinder.Axis) <= linearTolerance;
        }

        public bool FaceCoversAxisAt(IFace2 face, Vector3 axisPoint, double tolerance)
        {
            try
            {
                double[] closest = SwMath.ToDoubleArray(face?.GetClosestPointOn(axisPoint.X, axisPoint.Y, axisPoint.Z));
                if (closest.Length < 3)
                {
                    return false;
                }

                var point = new Vector3(closest[0], closest[1], closest[2]);
                return SwMath.Distance(point, axisPoint) <= tolerance;
            }
            catch (Exception ex)
            {
                Logger.Error("验证孔端面是否覆盖轴线时失败。", ex);
                return false;
            }
        }

        private static void AddVertexStation(IVertex vertex, Vector3 origin, Vector3 axis, ref double min, ref double max)
        {
            if (vertex == null || !SwMath.TryReadPoint(vertex.GetPoint(), out Vector3 point))
            {
                return;
            }

            double station = SwMath.ProjectStation(point, origin, axis);
            min = Math.Min(min, station);
            max = Math.Max(max, station);
        }
    }
}
