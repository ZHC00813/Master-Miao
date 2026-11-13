using System.Collections.Generic;
using Blankedge.SWHoleTools.Geometry;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.Model
{
    public sealed class HoleGroup
    {
        public IFace2 SelectedFace { get; set; }
        public IFace2 PrimaryCylinderFace { get; set; }
        public IBody2 Body { get; set; }
        public Vector3 AxisOrigin { get; set; }
        public Vector3 Axis { get; set; }
        public double MainRadius { get; set; }
        public double MinStation { get; set; }
        public double MaxStation { get; set; }
        public IList<IFace2> Faces { get; } = new List<IFace2>();
        public IList<CylindricalFaceInfo> Cylinders { get; } = new List<CylindricalFaceInfo>();
        public IList<ConicalFaceInfo> Cones { get; } = new List<ConicalFaceInfo>();
        public IList<BoundaryConnection> Boundaries { get; } = new List<BoundaryConnection>();

        public double AxialLength => System.Math.Max(0.0, MaxStation - MinStation);
    }

    public sealed class BoundaryConnection
    {
        public IFace2 CurvedFace { get; set; }
        public IFace2 BoundaryFace { get; set; }
        public IEdge SharedEdge { get; set; }
        public double Station { get; set; }
        public double Radius { get; set; }
    }

    public sealed class HoleAnalysis
    {
        public HoleDefinition Definition { get; set; }
        public HoleGroup Group { get; set; }
        public string Evidence { get; set; } = string.Empty;
    }
}
