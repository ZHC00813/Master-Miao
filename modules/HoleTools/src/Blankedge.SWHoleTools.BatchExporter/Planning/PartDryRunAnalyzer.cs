using System;
using Blankedge.SWHoleTools.BatchExporter.Model;

namespace Blankedge.SWHoleTools.BatchExporter.Planning
{
    public sealed class PartDryRunAnalyzer
    {
        public PartDryRunReport Analyze(PartDryRunSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            ModelBounds bounds = snapshot.Bounds ?? new ModelBounds();
            var report = new PartDryRunReport
            {
                SourcePath = snapshot.SourcePath ?? string.Empty,
                Envelope = bounds,
                Length = Max(bounds.X, bounds.Y, bounds.Z),
                Width = Middle(bounds.X, bounds.Y, bounds.Z),
                Height = Math.Min(bounds.X, Math.Min(bounds.Y, bounds.Z))
            };

            double shaftConfidence = ScoreShaft(snapshot.Shaft);
            double plateConfidence = ScorePlate(snapshot.Plate);

            if (snapshot.BodyCount > 1)
            {
                Escalate(report, DryRunStatus.Review);
                report.ReviewReasons.Add("检测到多实体；P0 Dry Run 需要明确主实体策略后再自动承诺类别。");
            }

            if (shaftConfidence >= 0.70 && Math.Abs(shaftConfidence - plateConfidence) <= 0.10)
            {
                report.Category = PartCategory.Unknown;
                report.Confidence = Math.Max(shaftConfidence, plateConfidence);
                Escalate(report, DryRunStatus.Review);
                report.ReviewReasons.Add("轴件和平板件证据接近；需要人工确认主分类。");
            }
            else if (shaftConfidence >= 0.70 && shaftConfidence > plateConfidence)
            {
                ApplyShaft(report, snapshot.Shaft, shaftConfidence);
            }
            else if (plateConfidence >= 0.70)
            {
                ApplyPlate(report, snapshot.Plate, plateConfidence);
            }
            else
            {
                report.Category = PartCategory.Prismatic;
                report.Confidence = bounds.X > 0.000001 || bounds.Y > 0.000001 || bounds.Z > 0.000001 ? 0.55 : 0.0;
                Escalate(report, DryRunStatus.Review);
                report.ReviewReasons.Add("缺少足够的共轴回转面或对置主平面证据；暂按普通棱柱件候选处理。");
            }

            foreach (HoleDryRunItem hole in snapshot.Holes)
            {
                NormalizeHole(hole);
                report.Holes.Add(hole);
                if (hole.Confidence < 0.80 || hole.Source == HoleSemanticSource.Unknown || hole.ReviewReasons.Count > 0)
                {
                    Escalate(report, DryRunStatus.Review);
                    report.ReviewReasons.Add("孔 " + (string.IsNullOrWhiteSpace(hole.HoleId) ? "(未编号)" : hole.HoleId) + " 语义需要复核。");
                }
            }

            if (report.ReviewReasons.Count > 0 && report.Status == DryRunStatus.Pass)
                report.Status = DryRunStatus.Review;
            if (report.ReviewReasons.Count == 0 && report.Status == DryRunStatus.Review)
                report.Status = DryRunStatus.Pass;

            return report;
        }

        private static void ApplyShaft(PartDryRunReport report, ShaftEvidence evidence, double confidence)
        {
            report.Category = PartCategory.Shaft;
            report.Status = confidence >= 0.85 ? DryRunStatus.Pass : DryRunStatus.Review;
            report.Confidence = confidence;
            report.PrimaryAxis = evidence.AxisName ?? string.Empty;
            report.Length = evidence.Length;
            report.Width = evidence.RadialEnvelopeDiameter;
            report.Height = evidence.RadialEnvelopeDiameter;
            report.MaxNominalRevolvedDiameter = evidence.MaxRevolvedDiameter;
            report.MaxRadialEnvelopeDiameter = evidence.RadialEnvelopeDiameter;
            report.RadialEnvelopeIsNonRound = evidence.HasNonRoundEnvelope;
            if (confidence < 0.85)
                report.ReviewReasons.Add("轴件证据未达到自动通过阈值 0.85。");
            if (evidence.HasNonRoundEnvelope)
                report.ReviewReasons.Add("径向包络包含非圆凸出，最大包络不得作为直径符号尺寸。");
        }

        private static void ApplyPlate(PartDryRunReport report, PlateEvidence evidence, double confidence)
        {
            report.Category = evidence.IsNativeSheetMetal ? PartCategory.SheetMetal : PartCategory.Plate;
            report.Status = confidence >= 0.90 ? DryRunStatus.Pass : DryRunStatus.Review;
            report.Confidence = confidence;
            report.PrimaryAxis = evidence.NormalAxisName ?? string.Empty;
            report.Length = Math.Max(evidence.Length, evidence.Width);
            report.Width = Math.Min(evidence.Length, evidence.Width);
            report.Height = evidence.Thickness;
            if (confidence < 0.90)
                report.ReviewReasons.Add("板件厚度或主平面证据未达到自动通过阈值 0.90。");
        }

        private static double ScoreShaft(ShaftEvidence evidence)
        {
            if (evidence == null) return 0.0;
            double diameter = Math.Max(0.000001, evidence.RadialEnvelopeDiameter);
            double offsetLimit = Math.Max(0.00005, 0.001 * diameter);

            double score = 0.0;
            score += 0.35 * Ramp((evidence.Length / diameter), 2.0, 2.5);
            score += 0.25 * Ramp(evidence.RevolvedSurfaceRatio, 0.30, 0.45);
            score += 0.15 * ReverseRamp(evidence.AxisAngleDeviationDegrees, 0.5, 2.0);
            score += 0.15 * ReverseRamp(evidence.AxisOffsetMeters, offsetLimit, offsetLimit * 3.0);
            score += 0.10 * (evidence.CompetingAxisCount <= 1 ? 1.0 : evidence.CompetingAxisCount == 2 ? 0.5 : 0.0);
            return Clamp01(score);
        }

        private static double ScorePlate(PlateEvidence evidence)
        {
            if (evidence == null) return 0.0;
            double smallerSpan = Math.Max(0.000001, Math.Min(evidence.Length, evidence.Width));
            double variationLimit = Math.Max(0.00002, 0.005 * evidence.Thickness);

            double score = 0.0;
            score += 0.25 * Ramp(evidence.MainPlaneAreaRatio, 0.50, 0.70);
            score += 0.20 * Ramp(evidence.OpposingPlaneOverlapRatio, 0.50, 0.70);
            score += 0.20 * ReverseRamp(evidence.Thickness / smallerSpan, 0.20, 0.35);
            score += 0.20 * ReverseRamp(evidence.ThicknessVariation, variationLimit, variationLimit * 4.0);
            score += 0.15 * ReverseRamp(evidence.NormalAngleDeviationDegrees, 0.2, 1.0);
            if (evidence.IsNativeSheetMetal) score = Math.Max(score, 0.90);
            return Clamp01(score);
        }

        private static void NormalizeHole(HoleDryRunItem hole)
        {
            hole.Confidence = Clamp01(hole.Confidence);
            if (hole.Source == HoleSemanticSource.GeometryInferred && !string.IsNullOrWhiteSpace(hole.ThreadSize))
                hole.ReviewReasons.Add("螺纹规格不能仅由几何推断自动确认。");
        }

        private static void Escalate(PartDryRunReport report, DryRunStatus status)
        {
            if ((int)status > (int)report.Status) report.Status = status;
        }

        private static double Ramp(double value, double low, double high)
        {
            if (value <= low) return 0.0;
            if (value >= high) return 1.0;
            return (value - low) / (high - low);
        }

        private static double ReverseRamp(double value, double good, double bad)
        {
            if (value <= good) return 1.0;
            if (value >= bad) return 0.0;
            return 1.0 - ((value - good) / (bad - good));
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        private static double Max(double a, double b, double c)
        {
            return Math.Max(a, Math.Max(b, c));
        }

        private static double Middle(double a, double b, double c)
        {
            return a + b + c - Max(a, b, c) - Math.Min(a, Math.Min(b, c));
        }
    }
}
