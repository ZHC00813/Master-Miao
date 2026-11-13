using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.BatchExporter.Model;
using Blankedge.SWHoleTools.BatchExporter.Planning;
using Blankedge.SWHoleTools.Drawing;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    public sealed class DrawingExportEngine
    {
        private readonly ISldWorks _application;
        private readonly DrawingLayoutPlanner _planner = new DrawingLayoutPlanner();

        public DrawingExportEngine(ISldWorks application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
        }

        public ExportResult Export(string sourcePath, BatchExportOptions options, CancellationToken cancellationToken)
        {
            string fullSource = Path.GetFullPath(sourcePath ?? string.Empty);
            if (!File.Exists(fullSource)) return ExportResult.Failed(fullSource, "源零件不存在。");
            if (!string.Equals(Path.GetExtension(fullSource), ".sldprt", StringComparison.OrdinalIgnoreCase))
                return ExportResult.Failed(fullSource, "第一版只接受 .SLDPRT 零件。");

            var validation = options.Validate();
            if (validation.Count > 0) return ExportResult.Failed(fullSource, string.Join("；", validation));
            IList<string> requestedOutputs;
            try { requestedOutputs = options.OutputPaths(fullSource); }
            catch (Exception ex) { return ExportResult.Failed(fullSource, ex.Message); }
            if (!options.DryRunOnly && !options.OverwriteExisting && requestedOutputs.Any(File.Exists))
                return ExportResult.Skip(fullSource, "输出文件已存在；未启用覆盖，因此跳过。");

            IModelDoc2 partModel = null;
            IModelDoc2 drawingModel = null;
            bool openedPart = false;
            ExportResult result = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                partModel = _application.IGetOpenDocumentByName2(fullSource);
                if (partModel == null)
                {
                    int openErrors = 0;
                    int openWarnings = 0;
                    int flags = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
                    partModel = _application.OpenDoc6(fullSource, (int)swDocumentTypes_e.swDocPART, flags, string.Empty, ref openErrors, ref openWarnings);
                    openedPart = partModel != null;
                    if (partModel == null)
                        return ExportResult.Failed(fullSource, "SOLIDWORKS 无法以只读方式打开零件。错误码：" + openErrors);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ModelBounds bounds = ReadBounds(partModel);
                PartDryRunReport dryRunReport = new PartDryRunAnalyzer().Analyze(new PartDryRunSnapshot
                {
                    SourcePath = fullSource,
                    Bounds = bounds
                });
                if (options.DryRunOnly)
                    return new ExportResult { SourcePath = fullSource, Success = true, DryRunReport = dryRunReport, Message = dryRunReport.Summary() };

                DrawingLayoutPlan plan = _planner.Plan(bounds, options);
                if (!plan.Fits)
                    return ExportResult.Failed(fullSource, plan.Warning);

                string template = ResolveTemplate(options, plan.Paper);
                if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                    return ExportResult.Failed(fullSource, "没有可用的 SOLIDWORKS 工程图模板；请在设置中指定 .DRWDOT。");

                drawingModel = _application.NewDocument(template, ToPaperSize(plan.Paper.Choice), plan.Paper.Width, plan.Paper.Height) as IModelDoc2;
                if (!(drawingModel is IDrawingDoc drawing))
                    return ExportResult.Failed(fullSource, "创建工程图文档失败。");

                var sheet = drawing.GetCurrentSheet() as ISheet;
                if (sheet == null || !sheet.SetScale(plan.Scale, 1.0, false, false))
                    throw new InvalidOperationException("无法设置工程图图纸比例。");

                var availableViews = ((Array)partModel.GetModelViewNames()).Cast<string>().ToArray();
                foreach (PlannedView planned in plan.Views)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string modelViewName = ModelViewNames.Resolve(planned.ModelViewName, availableViews);
                    IView view = drawing.CreateDrawViewFromModelView3(fullSource, modelViewName, planned.CenterX, planned.CenterY, 0.0);
                    if (view == null)
                        throw new InvalidOperationException("无法创建模型视图：" + planned.ModelViewName);
                    view.UseSheetScale = 0;
                    view.ScaleDecimal = plan.Scale;
                }
                // SW automatic view insertion can alter the sheet scale after its initial assignment.
                if (!sheet.SetScale(plan.Scale, 1.0, false, false))
                    throw new InvalidOperationException("无法保持指定的图纸比例。");
                drawingModel.EditRebuild3();

                int annotations = 0;
                if (options.AddHoleAnnotations)
                {
                    var attributes = new HoleAttributeManager(_application);
                    var resolver = new DrawingHoleResolver(attributes, new PersistentReferenceManager(new GeometricFaceFallbackResolver()));
                    annotations = new HoleAnnotationGenerator().CreateNotes(drawingModel, resolver.Resolve(drawingModel));
                    drawingModel.EditRebuild3();
                }

                result = new ExportResult { SourcePath = fullSource, Success = true, Plan = plan, DryRunReport = dryRunReport, AnnotationCount = annotations };
                foreach (string path in requestedOutputs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!options.OverwriteExisting && File.Exists(path)) throw new IOException("输出文件已存在：" + path);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                    object exportData = null;
                    if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        var pdfData = _application.GetExportFileData((int)swExportDataFileType_e.swExportPdfData) as IExportPdfData;
                        if (pdfData == null || !pdfData.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportSpecifiedSheets, drawing.GetSheetNames()))
                            throw new InvalidOperationException("无法初始化 PDF 导出设置。");
                        pdfData.ViewPdfAfterSaving = false;
                        exportData = pdfData;
                    }
                    drawingModel.ClearSelection2(true);
                    Save(drawingModel, path, exportData);
                    result.OutputPaths.Add(path);
                }
                result.Message = string.Format("{0}，比例 {1:0.###}:1，孔标注 {2} 个。", plan.Paper.Name, plan.Scale, annotations);
                return result;
            }
            catch (OperationCanceledException)
            {
                if (result != null) { result.Success = false; result.Skipped = true; result.Message = "已取消；已完成的输出保留。"; return result; }
                return ExportResult.Skip(fullSource, "已取消。");
            }
            catch (Exception ex)
            {
                if (result != null) { result.Success = false; result.Message = ex.Message; return result; }
                return ExportResult.Failed(fullSource, ex.Message);
            }
            finally
            {
                if (drawingModel != null)
                {
                    try { _application.CloseDoc(drawingModel.GetTitle()); } catch { }
                }
                if (openedPart && partModel != null)
                {
                    try { _application.CloseDoc(partModel.GetTitle()); } catch { }
                }
            }
        }

        private static ModelBounds ReadBounds(IModelDoc2 model)
        {
            if (!(model is IPartDoc part)) throw new InvalidOperationException("文档不是零件。");
            // true keeps SOLIDWORKS system units (meters), which is what the planner expects.
            object rawBox = part.GetPartBox(true);
            var box = (rawBox as double[]) ?? ToArray(rawBox);
            if (box == null || box.Length < 6) throw new InvalidOperationException("无法读取零件包围盒。");
            return new ModelBounds { MinX = box[0], MinY = box[1], MinZ = box[2], MaxX = box[3], MaxY = box[4], MaxZ = box[5] };
        }

        private string ResolveTemplate(BatchExportOptions options, PaperDefinition paper)
        {
            if (!string.IsNullOrWhiteSpace(options.DrawingTemplatePath)) return Path.GetFullPath(options.DrawingTemplatePath);
            return _application.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
        }

        private static int ToPaperSize(PaperChoice choice)
        {
            switch (choice)
            {
                case PaperChoice.A4: return (int)swDwgPaperSizes_e.swDwgPaperA4size;
                case PaperChoice.A3: return (int)swDwgPaperSizes_e.swDwgPaperA3size;
                case PaperChoice.A2: return (int)swDwgPaperSizes_e.swDwgPaperA2size;
                case PaperChoice.A1: return (int)swDwgPaperSizes_e.swDwgPaperA1size;
                case PaperChoice.A0: return (int)swDwgPaperSizes_e.swDwgPaperA0size;
                default: throw new ArgumentOutOfRangeException(nameof(choice));
            }
        }

        private static void Save(IModelDoc2 model, string path, object exportData)
        {
            int errors = 0;
            int warnings = 0;
            bool saved = model.Extension.SaveAs(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, exportData, ref errors, ref warnings);
            if (!saved || errors != 0)
                throw new InvalidOperationException("保存失败：" + path + "，错误码：" + errors + "，警告码：" + warnings);
        }

        private static double[] ToArray(object value)
        {
            if (!(value is Array array)) return null;
            var result = new double[array.Length];
            for (int i = 0; i < array.Length; i++) result[i] = Convert.ToDouble(array.GetValue(i));
            return result;
        }
    }
}
