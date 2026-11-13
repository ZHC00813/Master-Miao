using System;
using System.Collections.Generic;

namespace Blankedge.SWHoleTools.BatchExporter.Model
{
    public enum DryRunStatus { Pass, Warn, Review, Fail }
    public enum PartCategory { Unknown, Shaft, Plate, SheetMetal, Prismatic }
    public enum HoleSemanticSource { PersistentAttribute, Feature, ColorFallback, GeometryInferred, Unknown }

    public sealed class ShaftEvidence
    {
        public string AxisName { get; set; } = string.Empty;
        public double Length { get; set; }
        public double RadialEnvelopeDiameter { get; set; }
        public double MaxRevolvedDiameter { get; set; }
        public double RevolvedSurfaceRatio { get; set; }
        public double AxisAngleDeviationDegrees { get; set; }
        public double AxisOffsetMeters { get; set; }
        public int CompetingAxisCount { get; set; }
        public bool HasNonRoundEnvelope { get; set; }
    }

    public sealed class PlateEvidence
    {
        public string NormalAxisName { get; set; } = string.Empty;
        public double Length { get; set; }
        public double Width { get; set; }
        public double Thickness { get; set; }
        public double MainPlaneAreaRatio { get; set; }
        public double OpposingPlaneOverlapRatio { get; set; }
        public double ThicknessVariation { get; set; }
        public double NormalAngleDeviationDegrees { get; set; }
        public bool IsNativeSheetMetal { get; set; }
    }

    public sealed class HoleDryRunItem
    {
        public string HoleId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ThreadSize { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public double Diameter { get; set; }
        public double Depth { get; set; }
        public HoleSemanticSource Source { get; set; } = HoleSemanticSource.Unknown;
        public double Confidence { get; set; }
        public IList<string> ReviewReasons { get; } = new List<string>();
    }

    public sealed class PartDryRunSnapshot
    {
        public string SourcePath { get; set; } = string.Empty;
        public ModelBounds Bounds { get; set; }
        public int BodyCount { get; set; } = 1;
        public ShaftEvidence Shaft { get; set; }
        public PlateEvidence Plate { get; set; }
        public IList<HoleDryRunItem> Holes { get; } = new List<HoleDryRunItem>();
    }

    public sealed class PartDryRunReport
    {
        public string SchemaVersion { get; set; } = "Blankedge.SWHoleTools.DryRun/0.1";
        public string SourcePath { get; set; } = string.Empty;
        public PartCategory Category { get; set; } = PartCategory.Unknown;
        public DryRunStatus Status { get; set; } = DryRunStatus.Review;
        public double Confidence { get; set; }
        public string PrimaryAxis { get; set; } = string.Empty;
        public ModelBounds Envelope { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double MaxNominalRevolvedDiameter { get; set; }
        public double MaxRadialEnvelopeDiameter { get; set; }
        public bool RadialEnvelopeIsNonRound { get; set; }
        public IList<string> ReviewReasons { get; } = new List<string>();
        public IList<HoleDryRunItem> Holes { get; } = new List<HoleDryRunItem>();

        public string Summary()
        {
            return string.Format("Dry Run {0}/{1} confidence={2:0.00} holes={3}", Status, Category, Confidence, Holes.Count);
        }
    }
}
