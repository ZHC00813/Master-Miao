using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Blankedge.SWHoleTools.Standards;
using Blankedge.SWHoleTools.BatchExporter.Configuration;
using Blankedge.SWHoleTools.BatchExporter.Model;
using Blankedge.SWHoleTools.BatchExporter.Planning;
using Blankedge.SWHoleTools.BatchExporter.SolidWorks;
using Blankedge.SWHoleTools.Drawing;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.UI;
using Blankedge.SWHoleTools.Utilities;

namespace Blankedge.SWHoleTools.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        [STAThread]
        private static int Main(string[] args)
        {
            if ((args.Length == 2 || args.Length == 3) && args[0] == "--hole-color-smoke") return HoleColorSmoke.Run(args[1], args.Length == 3 ? args[2] : null);
            if (args.Length > 0 && args[0] == "--existing-readonly")
            {
                try
                {
                    using (var session = SolidWorksSession.Connect(SolidWorksConnectionMode.ExistingOnly))
                    {
                        Console.WriteLine("REVISION " + session.Application.RevisionNumber());
                        Console.WriteLine("DOCUMENTS " + session.Application.GetDocumentCount());
                    }
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine(ex); return 1; }
            }
            if (args.Length > 0 && string.Equals(args[0], "--com-smoke", StringComparison.OrdinalIgnoreCase))
            {
                return RunComSmoke();
            }
            if (args.Length > 0 && string.Equals(args[0], "--existing-only-safety", StringComparison.OrdinalIgnoreCase))
            {
                return RunExistingOnlySafety();
            }

            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "Blankedge.SWHoleTools.Tests");
            string sandbox = Path.Combine(root, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            Environment.SetEnvironmentVariable("BLANKEDGE_HOLETOOLS_DATA_DIR", sandbox);
            Environment.SetEnvironmentVariable("TEMP", sandbox);
            Environment.SetEnvironmentVariable("TMP", sandbox);

            Run("Layout_AutoSmall_A2_1to1", LayoutAutoSmall);
            Run("Layout_AutoMedium_A0_1to1", LayoutAutoMedium);
            Run("Layout_AutoLarge_A1_1to5", LayoutAutoLarge);
            Run("Layout_FixedA4_RejectOverflow", LayoutRejectOverflow);
            Run("DryRun_ShaftHighConfidence", DryRunShaftHighConfidence);
            Run("DryRun_ShaftCandidateReview", DryRunShaftCandidateReview);
            Run("DryRun_PlateHighConfidence", DryRunPlateHighConfidence);
            Run("DryRun_GeometryInferredThreadReview", DryRunGeometryInferredThreadReview);
            Run("DryRun_UnnamedHoleReview", DryRunUnnamedHoleReview);
            Run("Options_Validation", OptionsValidation);
            Run("DrawingQueue_ImportPrefixFormatsScaleUi", () => DrawingQueueTests.Run(sandbox));
            Run("Annotation_ThreadedThrough", AnnotationThreadedThrough);
            Run("Annotation_NormalBlind", AnnotationNormalBlind);
            Run("Annotation_EndTypeFromGeometry", ThreadTerminationRegression.Run);
            Run("Tag_ApplyAndTemplate", TagApplyAndTemplate);
            Run("TagCatalog_SandboxPersistence", TagCatalogPersistence);
            Run("TagCatalog_UniqueRgbRoundTrip", TagCatalogUniqueRgbRoundTrip);
            Run("ViewPreset_SandboxPersistence", ViewPresetPersistence);
            Run("CommandIcons_SandboxGeneration", CommandIconsGeneration);
            Run("HoleDialog_CancelThenReopenAndApply", HoleDialogCancelThenReopenAndApply);
            Run("Logger_SandboxWrite", LoggerSandboxWrite);

            Console.WriteLine();
            Console.WriteLine("RESULT passed={0} failed={1}", _passed, _failed);
            Console.WriteLine("SANDBOX " + sandbox);
            return _failed == 0 ? 0 : 1;
        }

        private static void HoleDialogCancelThenReopenAndApply()
        {
            var hole = new HoleDefinition { Tolerance = "6H", ThreadSize = "M3", Pitch = 0.0005 };
            foreach (var result in new[] { DialogResult.Cancel, DialogResult.OK })
            {
                using (var dialog = new HoleMarkDialog(hole, "", HoleTagCatalog.Load(), new HoleStandardDatabase()))
                {
                    dialog.Shown += (sender, args) => dialog.BeginInvoke(new Action(() =>
                    {
                        var button = result == DialogResult.OK ? dialog.AcceptButton : dialog.CancelButton;
                        button.PerformClick();
                    }));
                    Equal(result, dialog.ShowDialog());
                    Equal("M3", hole.ThreadSize);
                    Equal(0.0005, hole.Pitch);
                }
            }
        }

        private static void TagCatalogUniqueRgbRoundTrip()
        {
            var catalog = HoleTagCatalog.Load();
            for (int i = 0; i < 100; i++)
            {
                var tag = new HoleTagDefinition { Name = "Auto " + i };
                catalog.AssignAvailableColor(tag);
                True(catalog.FindByRgb(tag.Red, tag.Green, tag.Blue) == null, "allocated color already exists");
                catalog.Items.Add(tag);
            }
            catalog.Save();
            var restored = HoleTagCatalog.Load();
            foreach (var tag in catalog.Items)
                Equal(tag.Id, restored.FindByRgb(tag.Red, tag.Green, tag.Blue).Id);
            var first = restored.Items[0];
            restored.Items.Add(new HoleTagDefinition { Red = first.Red, Green = first.Green, Blue = first.Blue });
            True(restored.FindByRgb(first.Red, first.Green, first.Blue) == null, "ambiguous RGB must not resolve");
            bool rejected = false;
            try { restored.Save(); } catch (InvalidOperationException) { rejected = true; }
            True(rejected, "duplicate RGB must not be saved");
        }

        private static int RunComSmoke()
        {
            try
            {
                using (SolidWorksSession session = SolidWorksSession.Connect(SolidWorksConnectionMode.StartHidden))
                {
                    string partTemplate = session.Application.GetDocumentTemplate(1, string.Empty, 0, 0.0, 0.0);
                    string drawingTemplate = session.Application.GetDocumentTemplate(3, string.Empty, 6, 0.297, 0.210);
                    Console.WriteLine("REVISION " + session.Application.RevisionNumber());
                    Console.WriteLine("VISIBLE " + session.Application.Visible);
                    Console.WriteLine("PART_TEMPLATE " + partTemplate);
                    Console.WriteLine("PART_TEMPLATE_EXISTS " + File.Exists(partTemplate));
                    Console.WriteLine("DRAWING_TEMPLATE " + drawingTemplate);
                    Console.WriteLine("DRAWING_TEMPLATE_EXISTS " + File.Exists(drawingTemplate));
                    if (!File.Exists(partTemplate) || !File.Exists(drawingTemplate)) return 2;
                }
                Console.WriteLine("COM_SMOKE PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("COM_SMOKE FAIL " + ex);
                return 1;
            }
        }

        private static int RunExistingOnlySafety()
        {
            try
            {
                using (SolidWorksSession ignored = SolidWorksSession.Connect(SolidWorksConnectionMode.ExistingOnly))
                {
                    Console.WriteLine("EXISTING_ONLY FAIL unexpectedly connected");
                    return 1;
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("EXISTING_ONLY PASS " + ex.Message);
                return 0;
            }
        }

        private static void LayoutAutoSmall()
        {
            DrawingLayoutPlan plan = Plan(0.10, 0.05, 0.02, new BatchExportOptions());
            Equal(PaperChoice.A2, plan.Paper.Choice);
            Near(1.0, plan.Scale);
            True(plan.Fits, "small model must fit");
            Equal(4, plan.Views.Count);
        }

        private static void LayoutAutoMedium()
        {
            DrawingLayoutPlan plan = Plan(0.35, 0.20, 0.10, new BatchExportOptions());
            Equal(PaperChoice.A0, plan.Paper.Choice);
            Near(1.0, plan.Scale);
            True(plan.Fits, "medium model must fit");
        }

        private static void LayoutAutoLarge()
        {
            DrawingLayoutPlan plan = Plan(1.20, 0.60, 0.30, new BatchExportOptions());
            Equal(PaperChoice.A1, plan.Paper.Choice);
            Near(0.2, plan.Scale);
            True(plan.Fits, "large model must fit at reduced scale");
        }

        private static void LayoutRejectOverflow()
        {
            var options = new BatchExportOptions { Paper = PaperChoice.A4, ScaleMode = ScaleChoice.Fixed, FixedScale = 1.0 };
            DrawingLayoutPlan plan = Plan(0.50, 0.30, 0.20, options);
            True(!plan.Fits, "fixed overflowing layout must be rejected");
            True(!string.IsNullOrWhiteSpace(plan.Warning), "overflow needs a diagnostic");
        }

        private static void OptionsValidation()
        {
            var invalid = new BatchExportOptions { OutputDirectory = string.Empty, SaveDrawing = false, SavePdf = false };
            True(invalid.Validate().Count >= 2, "missing output and format must both be reported");
            var dryRun = new BatchExportOptions { DryRunOnly = true, SaveDrawing = false, SavePdf = false };
            Equal(0, dryRun.Validate().Count);
            var valid = new BatchExportOptions { OutputDirectory = Environment.GetEnvironmentVariable("BLANKEDGE_HOLETOOLS_DATA_DIR") };
            Equal(0, valid.Validate().Count);
        }

        private static void DryRunShaftHighConfidence()
        {
            var snapshot = new PartDryRunSnapshot
            {
                Bounds = new ModelBounds { MaxX = 0.30, MaxY = 0.04, MaxZ = 0.04 },
                Shaft = new ShaftEvidence
                {
                    AxisName = "X",
                    Length = 0.30,
                    RadialEnvelopeDiameter = 0.04,
                    MaxRevolvedDiameter = 0.035,
                    RevolvedSurfaceRatio = 0.62,
                    AxisAngleDeviationDegrees = 0.1,
                    AxisOffsetMeters = 0.000005,
                    CompetingAxisCount = 1
                }
            };
            snapshot.Holes.Add(new HoleDryRunItem { HoleId = "BH-000001", Source = HoleSemanticSource.PersistentAttribute, ThreadSize = "M5", Kind = "TappedThrough", Confidence = 1.0 });

            PartDryRunReport report = new PartDryRunAnalyzer().Analyze(snapshot);
            Equal(PartCategory.Shaft, report.Category);
            Equal(DryRunStatus.Pass, report.Status);
            True(report.Confidence >= 0.85, "shaft confidence too low");
            Near(0.30, report.Length);
            Near(0.035, report.MaxNominalRevolvedDiameter);
            Equal(1, report.Holes.Count);
        }

        private static void DryRunUnnamedHoleReview()
        {
            var snapshot = new PartDryRunSnapshot
            {
                Bounds = new ModelBounds { MaxX = 0.30, MaxY = 0.04, MaxZ = 0.04 },
                Shaft = new ShaftEvidence
                {
                    Length = 0.30, RadialEnvelopeDiameter = 0.04,
                    RevolvedSurfaceRatio = 0.62, CompetingAxisCount = 1
                }
            };
            snapshot.Holes.Add(new HoleDryRunItem { Source = HoleSemanticSource.Unknown, Confidence = 0.2 });
            var report = new PartDryRunAnalyzer().Analyze(snapshot);
            Equal(PartCategory.Shaft, report.Category);
            Equal(DryRunStatus.Review, report.Status);
            True(report.ReviewReasons.Count > 0, "unnamed hole must retain review reason");
        }

        private static void DryRunShaftCandidateReview()
        {
            var snapshot = new PartDryRunSnapshot
            {
                Bounds = new ModelBounds { MaxX = 0.10, MaxY = 0.04, MaxZ = 0.04 },
                Shaft = new ShaftEvidence
                {
                    AxisName = "X",
                    Length = 0.10,
                    RadialEnvelopeDiameter = 0.04,
                    MaxRevolvedDiameter = 0.04,
                    RevolvedSurfaceRatio = 0.30,
                    AxisAngleDeviationDegrees = 0.8,
                    AxisOffsetMeters = 0.0,
                    CompetingAxisCount = 1
                }
            };

            PartDryRunReport report = new PartDryRunAnalyzer().Analyze(snapshot);
            Equal(PartCategory.Shaft, report.Category);
            Equal(DryRunStatus.Review, report.Status);
            True(report.ReviewReasons.Any(item => item.Contains("0.85")), "missing shaft review reason");
        }

        private static void DryRunPlateHighConfidence()
        {
            var snapshot = new PartDryRunSnapshot
            {
                Bounds = new ModelBounds { MaxX = 0.40, MaxY = 0.20, MaxZ = 0.008 },
                Plate = new PlateEvidence
                {
                    NormalAxisName = "Z",
                    Length = 0.40,
                    Width = 0.20,
                    Thickness = 0.008,
                    MainPlaneAreaRatio = 0.78,
                    OpposingPlaneOverlapRatio = 0.75,
                    ThicknessVariation = 0.00001,
                    NormalAngleDeviationDegrees = 0.05
                }
            };

            PartDryRunReport report = new PartDryRunAnalyzer().Analyze(snapshot);
            Equal(PartCategory.Plate, report.Category);
            Equal(DryRunStatus.Pass, report.Status);
            Near(0.008, report.Height);
        }

        private static void DryRunGeometryInferredThreadReview()
        {
            var snapshot = new PartDryRunSnapshot
            {
                Bounds = new ModelBounds { MaxX = 0.12, MaxY = 0.08, MaxZ = 0.04 }
            };
            snapshot.Holes.Add(new HoleDryRunItem { HoleId = "inferred", Source = HoleSemanticSource.GeometryInferred, ThreadSize = "M5", Confidence = 0.95 });

            PartDryRunReport report = new PartDryRunAnalyzer().Analyze(snapshot);
            Equal(DryRunStatus.Review, report.Status);
            True(report.Holes[0].ReviewReasons.Any(item => item.Contains("几何推断")), "geometry inferred thread must be reviewed");
        }

        private static void AnnotationThreadedThrough()
        {
            var hole = new HoleDefinition
            {
                HoleType = HoleSemanticType.Threaded,
                ThreadSize = "M5",
                Pitch = 0.0008,
                Tolerance = "6H",
                TerminationType = TerminationType.Through
            };
            string text = new HoleAnnotationGenerator().Generate(hole);
            True(text.Contains("M5×0.8-6H"), text);
            True(text.Contains("螺纹通孔"), text);
        }

        private static void AnnotationNormalBlind()
        {
            var hole = new HoleDefinition { Diameter = 0.0045, MeasuredDepth = 0.008, TerminationType = TerminationType.Blind };
            string text = new HoleAnnotationGenerator().Generate(hole);
            True(text.Contains("Ø4.5"), text);
            True(text.Contains("DEPTH 8"), text);
        }

        private static void TagApplyAndTemplate()
        {
            var tag = new HoleTagDefinition
            {
                Id = "red-m5",
                Name = "红色 M5",
                HoleType = HoleSemanticType.Threaded,
                ThreadStandard = "ISO Metric",
                ThreadSize = "M5",
                PitchMillimeters = 0.8,
                Tolerance = "6H",
                Red = 300,
                Green = -1,
                Blue = 25,
                AnnotationTemplate = "{Thread}-{Tolerance} {Termination}"
            };
            var hole = new HoleDefinition { TerminationType = TerminationType.Blind };
            tag.ApplyTo(hole);
            Equal("red-m5", hole.TagId);
            Equal("M5", hole.ThreadSize);
            Near(0.0008, hole.Pitch);
            Equal(255, hole.ColorRed);
            Equal(0, hole.ColorGreen);
            Equal("M5-6H 螺纹盲孔", hole.AnnotationText);
        }

        private static void TagCatalogPersistence()
        {
            HoleTagCatalog catalog = HoleTagCatalog.Load();
            True(catalog.Items.Any(item => item.ThreadSize == "M5"), "default M5 tag missing");
            catalog.Items.Add(new HoleTagDefinition { Id = "test-only", Name = "测试标签", ThreadSize = "M7" });
            catalog.Save();
            HoleTagCatalog reloaded = HoleTagCatalog.Load();
            True(reloaded.Find("test-only") != null, "custom tag did not round-trip");
            True(reloaded.FilePath.StartsWith(Environment.GetEnvironmentVariable("BLANKEDGE_HOLETOOLS_DATA_DIR"), StringComparison.OrdinalIgnoreCase), "catalog escaped sandbox");
        }

        private static void ViewPresetPersistence()
        {
            ViewPresetStore store = ViewPresetStore.Load();
            store.UpsertAndSave(new ViewPreset { Name = "测试视图", ModelViewNames = new List<string> { "*Front", "InspectionView" } });
            ViewPresetStore reloaded = ViewPresetStore.Load();
            ViewPreset preset = reloaded.Presets.FirstOrDefault(item => item.Name == "测试视图");
            True(preset != null, "view preset did not round-trip");
            Equal(2, preset.ModelViewNames.Count);
            True(reloaded.FilePath.StartsWith(Environment.GetEnvironmentVariable("BLANKEDGE_HOLETOOLS_DATA_DIR"), StringComparison.OrdinalIgnoreCase), "preset escaped sandbox");
        }

        private static void CommandIconsGeneration()
        {
            CommandIconSet icons = CommandIconProvider.EnsureCreated();
            True(icons != null, "icon set is null");
            Equal(6, icons.MainIcons.Length);
            Equal(6, icons.CommandStrips.Length);
            for (int i = 0; i < icons.MainIcons.Length; i++)
            {
                True(File.Exists(icons.MainIcons[i]), "main icon missing: " + icons.MainIcons[i]);
                True(File.Exists(icons.CommandStrips[i]), "command strip missing: " + icons.CommandStrips[i]);
                using (var main = new Bitmap(icons.MainIcons[i]))
                using (var strip = new Bitmap(icons.CommandStrips[i]))
                {
                    Equal(main.Height, main.Width);
                    Equal(main.Height, strip.Height);
                    Equal(main.Width * 7, strip.Width);
                }
            }
            True(icons.MainIcons.All(path => path.StartsWith(Environment.GetEnvironmentVariable("TEMP"), StringComparison.OrdinalIgnoreCase)), "icons escaped sandbox");
        }

        private static void LoggerSandboxWrite()
        {
            Logger.Info("automated sandbox log test");
            True(File.Exists(Logger.LogPath), "log file was not created");
            True(Logger.LogPath.StartsWith(Environment.GetEnvironmentVariable("TEMP"), StringComparison.OrdinalIgnoreCase), "log escaped sandbox");
            True(File.ReadAllText(Logger.LogPath).Contains("automated sandbox log test"), "log line missing");
        }

        private static DrawingLayoutPlan Plan(double x, double y, double z, BatchExportOptions options)
        {
            options.Views = (ViewPreset)ViewPreset.Defaults()[0];
            return new DrawingLayoutPlanner().Plan(new ModelBounds { MaxX = x, MaxY = y, MaxZ = z }, options);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + " :: " + ex.Message);
            }
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("expected=" + expected + " actual=" + actual);
        }

        private static void Near(double expected, double actual)
        {
            if (Math.Abs(expected - actual) > 0.0000001)
                throw new InvalidOperationException("expected=" + expected + " actual=" + actual);
        }
    }
}
