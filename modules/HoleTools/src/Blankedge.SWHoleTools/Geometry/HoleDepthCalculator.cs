using System;
using System.Linq;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.Geometry
{
    public sealed class HoleDepthCalculator
    {
        public void Populate(HoleDefinition definition, HoleGroup group, TerminationAnalysis termination)
        {
            if (definition == null || group == null || termination == null)
            {
                return;
            }

            try
            {
                definition.TerminationType = termination.Type;
                if (termination.Type == TerminationType.Through || termination.Type == TerminationType.InterBody)
                {
                    definition.ThroughLength = group.AxialLength;
                }
                else if (termination.Type == TerminationType.Blind)
                {
                    PopulateBlindDepth(definition, group, termination);
                }

                PopulateCounterbore(definition, group);
                PopulateCones(definition, group, termination);
                Logger.Info($"孔深计算：MeasuredDepth={definition.MeasuredDepth:F6} m, ThroughLength={definition.ThroughLength:F6} m。" );
            }
            catch (Exception ex)
            {
                Logger.Error("计算孔深或附加结构失败。", ex);
                definition.TerminationType = TerminationType.Ambiguous;
            }
        }

        private static void PopulateBlindDepth(HoleDefinition definition, HoleGroup group, TerminationAnalysis termination)
        {
            double middle = (group.MinStation + group.MaxStation) * 0.5;
            if (termination.PositiveHit != null && termination.NegativeHit == null)
            {
                double bottom = middle + termination.PositiveHit.Distance;
                definition.MeasuredDepth = Math.Max(0.0, bottom - group.MinStation);
                definition.BottomType = ClassifyBottom(termination.PositiveHit.Face);
            }
            else if (termination.NegativeHit != null && termination.PositiveHit == null)
            {
                double bottom = middle - termination.NegativeHit.Distance;
                definition.MeasuredDepth = Math.Max(0.0, group.MaxStation - bottom);
                definition.BottomType = ClassifyBottom(termination.NegativeHit.Face);
            }

            definition.DrillDepth = definition.MeasuredDepth;
        }

        private static void PopulateCounterbore(HoleDefinition definition, HoleGroup group)
        {
            CylindricalFaceInfo counterbore = group.Cylinders
                .Where(item => item.Geometry.Radius > group.MainRadius * 1.02)
                .OrderByDescending(item => item.Geometry.Radius)
                .FirstOrDefault();

            if (counterbore == null)
            {
                return;
            }

            definition.HasCounterbore = true;
            definition.CounterboreDiameter = counterbore.Geometry.Radius * 2.0;
            definition.CounterboreDepth = Math.Max(0.0, counterbore.MaxStation - counterbore.MinStation);
        }

        private static void PopulateCones(HoleDefinition definition, HoleGroup group, TerminationAnalysis termination)
        {
            foreach (ConicalFaceInfo cone in group.Cones)
            {
                double axialLength = Math.Abs(cone.MaxStation - cone.MinStation);
                bool atMin = Math.Abs(cone.MinStation - group.MinStation) <= Math.Max(1e-6, group.MainRadius * 1e-3);
                bool atMax = Math.Abs(cone.MaxStation - group.MaxStation) <= Math.Max(1e-6, group.MainRadius * 1e-3);
                bool atOpenEnd = (atMin && termination.TopologyOpenAtMin) || (atMax && termination.TopologyOpenAtMax);

                if (atOpenEnd && cone.MajorRadius >= group.MainRadius * 1.20)
                {
                    definition.HasCountersink = true;
                    definition.CountersinkDiameter = Math.Max(definition.CountersinkDiameter, cone.MajorRadius * 2.0);
                    definition.CountersinkAngle = Math.Max(
                        definition.CountersinkAngle,
                        cone.Geometry.HalfAngleRadians * 2.0 * 180.0 / Math.PI);
                }
                else if (atOpenEnd && axialLength <= group.MainRadius * 0.75)
                {
                    definition.HasEntranceChamfer = true;
                }
                else if (definition.TerminationType == TerminationType.Blind && definition.BottomType == HoleBottomType.Unknown)
                {
                    definition.BottomType = HoleBottomType.Conical;
                }
            }
        }

        private static HoleBottomType ClassifyBottom(IFace2 face)
        {
            try
            {
                var surface = face?.GetSurface() as ISurface;
                if (surface == null)
                {
                    return HoleBottomType.Unknown;
                }

                if (surface.IsPlane())
                {
                    return HoleBottomType.Flat;
                }

                if (surface.IsCone())
                {
                    return HoleBottomType.Conical;
                }

                return HoleBottomType.Other;
            }
            catch
            {
                return HoleBottomType.Unknown;
            }
        }
    }
}
