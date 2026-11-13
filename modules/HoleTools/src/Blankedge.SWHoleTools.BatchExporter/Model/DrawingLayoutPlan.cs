using System;
using System.Collections.Generic;

namespace Blankedge.SWHoleTools.BatchExporter.Model
{
    public sealed class ModelBounds
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        public double X => Math.Max(0.000001, MaxX - MinX);
        public double Y => Math.Max(0.000001, MaxY - MinY);
        public double Z => Math.Max(0.000001, MaxZ - MinZ);
    }

    public sealed class PaperDefinition
    {
        public PaperChoice Choice { get; set; }
        public string Name { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class PlannedView
    {
        public string ModelViewName { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
    }

    public sealed class DrawingLayoutPlan
    {
        public PaperDefinition Paper { get; set; }
        public double Scale { get; set; }
        public double EstimatedRequiredWidth { get; set; }
        public double EstimatedRequiredHeight { get; set; }
        public bool Fits { get; set; }
        public string Warning { get; set; } = string.Empty;
        public IList<PlannedView> Views { get; set; } = new List<PlannedView>();
    }
}
