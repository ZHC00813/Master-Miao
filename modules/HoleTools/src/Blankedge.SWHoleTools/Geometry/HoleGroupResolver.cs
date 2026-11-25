using System;
using System.Collections.Generic;
using System.Linq;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.Geometry
{
    public sealed class HoleGroupResolver
    {
        private readonly FaceAnalyzer _faces;

        public HoleGroupResolver(FaceAnalyzer faces)
        {
            _faces = faces ?? throw new ArgumentNullException(nameof(faces));
        }

        public OperationResult<HoleGroup> Resolve(IFace2 selectedFace, CylinderGeometry selectedCylinder)
        {
            try
            {
                var body = selectedFace?.GetBody() as IBody2;
                if (body == null)
                {
                    return OperationResult<HoleGroup>.Fail("无法确定所选面的所属 Body。" );
                }

                double linearTolerance = Math.Max(1e-6, selectedCylinder.Radius * 1e-4);
                var group = new HoleGroup
                {
                    SelectedFace = selectedFace,
                    Body = body,
                    AxisOrigin = selectedCylinder.Origin,
                    Axis = selectedCylinder.Axis,
                    MainRadius = selectedCylinder.Radius
                };

                var queue = new Queue<IFace2>();
                var visited = new HashSet<long>();
                queue.Enqueue(selectedFace);

                while (queue.Count > 0)
                {
                    IFace2 current = queue.Dequeue();
                    long currentId = SwMath.GetComIdentity(current);
                    if (!visited.Add(currentId))
                    {
                        continue;
                    }

                    if (!TryAddCurvedFace(group, current, selectedCylinder, linearTolerance))
                    {
                        continue;
                    }

                    foreach (IEdge edge in SwMath.ToComList<IEdge>(current.GetEdges()))
                    {
                        CircularEdgeInfo circle = TryGetCircle(edge);
                        foreach (IFace2 adjacent in SwMath.ToComList<IFace2>(edge.GetTwoAdjacentFaces2()))
                        {
                            if (adjacent == null || SwMath.GetComIdentity(adjacent) == currentId)
                            {
                                continue;
                            }

                            if (IsCoaxialCurvedFace(adjacent, selectedCylinder, linearTolerance))
                            {
                                queue.Enqueue(adjacent);
                                continue;
                            }

                            AddBoundary(group, current, adjacent, edge, circle);
                            EnqueueCoaxialFacesAcrossBridge(adjacent, selectedCylinder, linearTolerance, visited, queue);
                        }
                    }
                }

                if (group.Cylinders.Count == 0)
                {
                    return OperationResult<HoleGroup>.Fail("未能构建包含圆柱面的 HoleGroup。" );
                }

                CylindricalFaceInfo primary = group.Cylinders.OrderBy(item => item.Geometry.Radius).First();
                group.PrimaryCylinderFace = primary.Face;
                group.MainRadius = primary.Geometry.Radius;

                IEnumerable<double> stations = group.Cylinders
                    .SelectMany(item => item.CircularEdges)
                    .Select(item => SwMath.ProjectStation(item.Center, group.AxisOrigin, group.Axis))
                    .Concat(group.Cones.SelectMany(item => item.CircularEdges)
                        .Select(item => SwMath.ProjectStation(item.Center, group.AxisOrigin, group.Axis)));

                double[] stationArray = stations.ToArray();
                if (stationArray.Length < 2)
                {
                    return OperationResult<HoleGroup>.Fail("孔边界不足，无法确定轴向范围。" );
                }

                group.MinStation = stationArray.Min();
                group.MaxStation = stationArray.Max();
                Logger.Info($"HoleGroup 构建完成：曲面 {group.Cylinders.Count + group.Cones.Count}，总面 {group.Faces.Count}。" );
                return OperationResult<HoleGroup>.Ok(group);
            }
            catch (Exception ex)
            {
                Logger.Error("HoleGroup 拓扑追踪失败。", ex);
                return OperationResult<HoleGroup>.Fail("HoleGroup 拓扑追踪失败：" + ex.Message);
            }
        }

        private static bool IsInner(IFace2 face) { return face != null && face.FaceInSurfaceSense(); }
        private bool TryAddCurvedFace(HoleGroup group, IFace2 face, CylinderGeometry reference, double tolerance)
        {
            if (IsInner(face) && _faces.TryGetCylinder(face, out CylinderGeometry cylinder, out _) && _faces.AreCoaxial(reference, cylinder, tolerance))
            {
                var info = new CylindricalFaceInfo { Face = face, Geometry = cylinder };
                foreach (CircularEdgeInfo edge in _faces.GetCircularEdges(face))
                {
                    info.CircularEdges.Add(edge);
                }

                _faces.TryGetAxialExtent(face, group.AxisOrigin, group.Axis, out double min, out double max);
                info.MinStation = min;
                info.MaxStation = max;
                group.Cylinders.Add(info);
                AddUniqueFace(group.Faces, face);
                return true;
            }

            if (IsInner(face) && _faces.TryGetCone(face, out ConeGeometry cone) && _faces.IsCoaxial(reference, cone, tolerance))
            {
                var info = new ConicalFaceInfo { Face = face, Geometry = cone };
                foreach (CircularEdgeInfo edge in _faces.GetCircularEdges(face))
                {
                    info.CircularEdges.Add(edge);
                }

                _faces.TryGetAxialExtent(face, group.AxisOrigin, group.Axis, out double min, out double max);
                info.MinStation = min;
                info.MaxStation = max;
                info.MajorRadius = info.CircularEdges.Count == 0 ? cone.RadiusAtOrigin : info.CircularEdges.Max(item => item.Radius);
                group.Cones.Add(info);
                AddUniqueFace(group.Faces, face);
                return true;
            }

            return false;
        }

        private bool IsCoaxialCurvedFace(IFace2 face, CylinderGeometry reference, double tolerance)
        {
            return IsInner(face) && ((_faces.TryGetCylinder(face, out CylinderGeometry cylinder, out _) && _faces.AreCoaxial(reference, cylinder, tolerance)) ||
                   (_faces.TryGetCone(face, out ConeGeometry cone) && _faces.IsCoaxial(reference, cone, tolerance)));
        }

        private void EnqueueCoaxialFacesAcrossBridge(
            IFace2 bridge,
            CylinderGeometry reference,
            double tolerance,
            ISet<long> visited,
            Queue<IFace2> queue)
        {
            if (!_faces.TryGetPlane(bridge, out PlaneGeometry plane) ||
                Math.Abs(Vector3.Dot(plane.Normal, reference.Axis)) < 0.95)
            {
                return;
            }

            try
            {
                foreach (IEdge bridgeEdge in SwMath.ToComList<IEdge>(bridge.GetEdges()))
                {
                    foreach (IFace2 candidate in SwMath.ToComList<IFace2>(bridgeEdge.GetTwoAdjacentFaces2()))
                    {
                        if (candidate != null && !visited.Contains(SwMath.GetComIdentity(candidate)) &&
                            IsCoaxialCurvedFace(candidate, reference, tolerance))
                        {
                            queue.Enqueue(candidate);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("跨过台阶平面追踪同轴面失败。", ex);
            }
        }

        private void AddBoundary(HoleGroup group, IFace2 curvedFace, IFace2 boundaryFace, IEdge edge, CircularEdgeInfo circle)
        {
            AddUniqueFace(group.Faces, boundaryFace);
            if (circle == null)
            {
                return;
            }

            group.Boundaries.Add(new BoundaryConnection
            {
                CurvedFace = curvedFace,
                BoundaryFace = boundaryFace,
                SharedEdge = edge,
                Station = SwMath.ProjectStation(circle.Center, group.AxisOrigin, group.Axis),
                Radius = circle.Radius
            });
        }

        private CircularEdgeInfo TryGetCircle(IEdge edge)
        {
            try
            {
                var curve = edge?.GetCurve() as ICurve;
                if (curve == null || !curve.IsCircle())
                {
                    return null;
                }

                double[] data = SwMath.ToDoubleArray(curve.CircleParams);
                return data.Length < 7 ? null : new CircularEdgeInfo
                {
                    Edge = edge,
                    Center = new Vector3(data[0], data[1], data[2]),
                    Axis = new Vector3(data[3], data[4], data[5]).Normalized(),
                    Radius = Math.Abs(data[6])
                };
            }
            catch
            {
                return null;
            }
        }

        private static void AddUniqueFace(IList<IFace2> faces, IFace2 candidate)
        {
            long id = SwMath.GetComIdentity(candidate);
            if (!faces.Any(face => SwMath.GetComIdentity(face) == id))
            {
                faces.Add(candidate);
            }
        }
    }
}
