using System;
using System.Collections.Generic;
using System.Linq;
using Blankedge.SWHoleTools.BatchExporter.Model;

namespace Blankedge.SWHoleTools.BatchExporter.Planning
{
    public sealed class DrawingLayoutPlanner
    {
        private const double Margin = 0.015;
        private const double ViewGap = 0.025;
        private const double TitleBlockHeight = 0.055;
        private static readonly double[] ConventionalScales = { 2.0, 1.0, 0.5, 0.2, 0.1, 0.05, 0.02 };

        public static readonly IList<PaperDefinition> Papers = new[]
        {
            new PaperDefinition { Choice = PaperChoice.A4, Name = "A4", Width = 0.297, Height = 0.210 },
            new PaperDefinition { Choice = PaperChoice.A3, Name = "A3", Width = 0.420, Height = 0.297 },
            new PaperDefinition { Choice = PaperChoice.A2, Name = "A2", Width = 0.594, Height = 0.420 },
            new PaperDefinition { Choice = PaperChoice.A1, Name = "A1", Width = 0.841, Height = 0.594 },
            new PaperDefinition { Choice = PaperChoice.A0, Name = "A0", Width = 1.189, Height = 0.841 }
        };

        public DrawingLayoutPlan Plan(ModelBounds bounds, BatchExportOptions options)
        {
            if (bounds == null) throw new ArgumentNullException(nameof(bounds));
            if (options == null) throw new ArgumentNullException(nameof(options));

            IList<PaperDefinition> candidatePapers = options.Paper == PaperChoice.Auto
                ? Papers
                : Papers.Where(item => item.Choice == options.Paper).ToArray();
            IList<double> candidateScales = options.ScaleMode == ScaleChoice.Fixed
                ? new[] { options.FixedScale }
                : ConventionalScales;

            // In full-auto mode, enlarge a very small part only when 2:1 still fits A4.
            // Otherwise preserve 1:1 as long as any A-series sheet can contain the views.
            if (options.Paper == PaperChoice.Auto && options.ScaleMode == ScaleChoice.AutoFit)
            {
                DrawingLayoutPlan enlargedA4 = Create(bounds, options.Views.ModelViewNames, Papers[0], 2.0);
                if (enlargedA4.Fits) return enlargedA4;
                candidateScales = ConventionalScales.Where(value => value <= 1.0).ToArray();
            }

            foreach (double scale in candidateScales)
            {
                foreach (PaperDefinition paper in candidatePapers)
                {
                    DrawingLayoutPlan plan = Create(bounds, options.Views.ModelViewNames, paper, scale);
                    if (plan.Fits) return plan;
                }
            }

            PaperDefinition fallbackPaper = candidatePapers.Last();
            double fallbackScale = candidateScales.Last();
            DrawingLayoutPlan fallback = Create(bounds, options.Views.ModelViewNames, fallbackPaper, fallbackScale);
            fallback.Warning = "所选纸张/比例不足以容纳估算视图；已生成计划，但导出前应调整设置。";
            return fallback;
        }

        private static DrawingLayoutPlan Create(ModelBounds bounds, IList<string> names, PaperDefinition paper, double scale)
        {
            var dimensions = names.Select(name => DimensionsFor(name, bounds)).ToArray();
            int count = Math.Max(1, dimensions.Length);
            int columns = count <= 2 ? count : 2;
            int rows = (int)Math.Ceiling(count / (double)columns);
            double maxCellWidth = dimensions.Length == 0 ? bounds.X : dimensions.Max(item => item.Item1);
            double maxCellHeight = dimensions.Length == 0 ? bounds.Z : dimensions.Max(item => item.Item2);
            double requiredWidth = ((columns * maxCellWidth) + ((columns - 1) * ViewGap / scale)) * scale;
            double requiredHeight = ((rows * maxCellHeight) + ((rows - 1) * ViewGap / scale)) * scale;
            double usableWidth = paper.Width - (2 * Margin);
            double usableHeight = paper.Height - (2 * Margin) - TitleBlockHeight;

            var plan = new DrawingLayoutPlan
            {
                Paper = paper,
                Scale = scale,
                EstimatedRequiredWidth = requiredWidth,
                EstimatedRequiredHeight = requiredHeight,
                Fits = requiredWidth <= usableWidth && requiredHeight <= usableHeight
            };

            for (int i = 0; i < names.Count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                double cellWidth = usableWidth / columns;
                double cellHeight = usableHeight / rows;
                plan.Views.Add(new PlannedView
                {
                    ModelViewName = names[i],
                    CenterX = Margin + ((column + 0.5) * cellWidth),
                    CenterY = Margin + TitleBlockHeight + ((rows - row - 0.5) * cellHeight)
                });
            }
            return plan;
        }

        private static Tuple<double, double> DimensionsFor(string viewName, ModelBounds bounds)
        {
            switch ((viewName ?? string.Empty).ToUpperInvariant())
            {
                case "*TOP": return Tuple.Create(bounds.X, bounds.Y);
                case "*RIGHT":
                case "*LEFT": return Tuple.Create(bounds.Y, bounds.Z);
                case "*ISOMETRIC": return Tuple.Create(Math.Sqrt((bounds.X * bounds.X) + (bounds.Y * bounds.Y)), Math.Sqrt((bounds.Z * bounds.Z) + (0.25 * bounds.Y * bounds.Y)));
                default: return Tuple.Create(bounds.X, bounds.Z);
            }
        }
    }
}
