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
            if (options.Category == ProductionCategory.木板件)
            {
                var wood = new DrawingLayoutPlan { Paper = new PaperDefinition { Choice = PaperChoice.Custom, Name = "木板 1:1 自定义", Width = bounds.X + bounds.Z + .17, Height = bounds.Y + bounds.Z + .20 }, Scale = 1, Fits = true };
                double frontX = .055 + bounds.X / 2;
                double frontY = wood.Paper.Height - .045 - bounds.Y / 2;
                wood.Views.Add(new PlannedView { ModelViewName = "*Front", CenterX = frontX, CenterY = frontY });
                wood.Views.Add(new PlannedView { ModelViewName = "*Left", CenterX = .115 + bounds.X + bounds.Z / 2, CenterY = frontY });
                wood.Views.Add(new PlannedView { ModelViewName = "*Top", CenterX = frontX, CenterY = .085 + bounds.Z / 2 });
                return wood;
            }

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
                DrawingLayoutPlan enlargedA4 = CreateForOptions(bounds, options, Papers[0], 2.0);
                if (enlargedA4.Fits) return enlargedA4;
                candidateScales = ConventionalScales.Where(value => value <= 1.0).ToArray();
            }

            foreach (double scale in candidateScales)
            {
                foreach (PaperDefinition paper in candidatePapers)
                {
                    DrawingLayoutPlan plan = CreateForOptions(bounds, options, paper, scale);
                    if (plan.Fits) return plan;
                }
            }

            PaperDefinition fallbackPaper = candidatePapers.Last();
            double fallbackScale = candidateScales.Last();
            DrawingLayoutPlan fallback = CreateForOptions(bounds, options, fallbackPaper, fallbackScale);
            fallback.Warning = "所选纸张/比例不足以容纳估算视图；已生成计划，但导出前应调整设置。";
            return fallback;
        }

        private static DrawingLayoutPlan CreateForOptions(ModelBounds bounds, BatchExportOptions options, PaperDefinition paper, double scale)
        {
            if (options.Category != ProductionCategory.焊接) return Create(bounds, options.Views.ModelViewNames, paper, scale);
            double diagonal = Math.Sqrt(bounds.X*bounds.X + bounds.Z*bounds.Z);
            bool fits = paper.Width >= .841 && diagonal*scale < paper.Width*.58 && (bounds.Y+Math.Max(bounds.X,bounds.Z)*.6)*scale < paper.Height*.44;
            var plan = new DrawingLayoutPlan { Paper=paper, Scale=scale, Fits=fits };
            plan.Views.Add(new PlannedView { ModelViewName="*Isometric", CenterX=paper.Width*.33, CenterY=paper.Height*.64 });
            plan.Views.Add(new PlannedView { ModelViewName="*Front", CenterX=paper.Width*.33, CenterY=paper.Height*.25 });
            return plan;
        }

        private static DrawingLayoutPlan Create(ModelBounds bounds, IList<string> names, PaperDefinition paper, double scale)
        {
            var dimensions = names.Select(name => DimensionsFor(name, bounds)).ToArray();
            int count = Math.Max(1, dimensions.Length);
            int columns = count <= 2 ? count : 2;
            int rows = (int)Math.Ceiling(count / (double)columns);
            var rowIndex = new int[count]; var colIndex = new int[count];
            var widths = new double[columns]; var heights = new double[rows];
            bool projected = count >= 3 && names.Contains("*Front") && names.Contains("*Top") && (names.Contains("*Left") || names.Contains("*Right"));
            for (int i = 0; i < names.Count; i++)
            {
                rowIndex[i] = i / columns; colIndex[i] = i % columns;
                if (projected)
                {
                    string name = names[i].ToUpperInvariant();
                    rowIndex[i] = names.Contains("*Left") ? (name == "*TOP" || name == "*ISOMETRIC" ? 1 : 0) : (name == "*TOP" || name == "*ISOMETRIC" ? 0 : 1);
                    colIndex[i] = name == "*FRONT" || name == "*TOP" ? 0 : 1;
                }
                widths[colIndex[i]] = Math.Max(widths[colIndex[i]], dimensions[i].Item1 * scale);
                heights[rowIndex[i]] = Math.Max(heights[rowIndex[i]], dimensions[i].Item2 * scale);
            }
            // Reserve space around geometry for dimensions and leaders rather than repeat the longest span in every cell.
            for (int i=0;i<columns;i++) widths[i] += .035;
            for (int i=0;i<rows;i++) heights[i] += .025;
            double requiredWidth = widths.Sum() + (columns-1)*ViewGap;
            double requiredHeight = heights.Sum() + (rows-1)*ViewGap;
            double usableWidth = paper.Width - 2*Margin;
            double usableHeight = paper.Height - 2*Margin - TitleBlockHeight;
            var plan = new DrawingLayoutPlan { Paper=paper, Scale=scale, EstimatedRequiredWidth=requiredWidth, EstimatedRequiredHeight=requiredHeight, Fits=requiredWidth<=usableWidth && requiredHeight<=usableHeight };
            double left = Margin + Math.Max(0,usableWidth-requiredWidth)/2;
            double top = paper.Height-Margin-Math.Max(0,usableHeight-requiredHeight)/2;
            for (int i=0;i<names.Count;i++)
            {
                int row=rowIndex[i], column=colIndex[i];
                plan.Views.Add(new PlannedView { ModelViewName=names[i], CenterX=left+widths.Take(column).Sum()+column*ViewGap+widths[column]/2, CenterY=top-heights.Take(row).Sum()-row*ViewGap-heights[row]/2 });
            }
            return plan;
        }
        private static Tuple<double, double> DimensionsFor(string viewName, ModelBounds bounds)
        {
            switch ((viewName ?? string.Empty).ToUpperInvariant())
            {
                case "*TOP": return Tuple.Create(bounds.X, bounds.Z);
                case "*RIGHT":
                case "*LEFT": return Tuple.Create(bounds.Z, bounds.Y);
                case "*ISOMETRIC": return Tuple.Create((bounds.X + bounds.Z) / Math.Sqrt(2), (bounds.X + bounds.Z) / Math.Sqrt(6) + bounds.Y * Math.Sqrt(2.0 / 3.0));
                default: return Tuple.Create(bounds.X, bounds.Y);
            }
        }
    }
}
