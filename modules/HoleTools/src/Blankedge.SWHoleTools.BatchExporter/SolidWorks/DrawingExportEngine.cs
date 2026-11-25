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
            if (!PartFileDiscovery.IsSupported(fullSource))
                return ExportResult.Failed(fullSource, "请选择 SLDPRT 零件或 SLDASM 装配体。");
            bool isAssembly = string.Equals(Path.GetExtension(fullSource), ".sldasm", StringComparison.OrdinalIgnoreCase);
            if (isAssembly && options.CopyPartModel)
                return ExportResult.Failed(fullSource, "装配体不能复制为单个零件，请取消配套 SLDPRT。");

            var validation = options.Validate();
            if (validation.Count > 0) return ExportResult.Failed(fullSource, string.Join("；", validation));
            IList<string> requestedOutputs;
            try { requestedOutputs = options.OutputPaths(fullSource); }
            catch (Exception ex) { return ExportResult.Failed(fullSource, ex.Message); }
            if (!options.DryRunOnly && !options.OverwriteExisting && requestedOutputs.Any(File.Exists))
                return ExportResult.Skip(fullSource, "输出文件已存在；未启用覆盖，因此跳过。");

            string originalSource = fullSource;
            IModelDoc2 partModel = null;
            IModelDoc2 drawingModel = null;
            bool openedPart = false;
            ExportResult result = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options.CopyPartModel && !options.DryRunOnly)
                {
                    var openedSource = _application.IGetOpenDocumentByName2(fullSource);
                    if (openedSource != null && openedSource.GetSaveFlag())
                        throw new InvalidOperationException("原零件有未保存的修改或孔标注，请先保存再生成配套模型。");
                    string copy = Path.GetFullPath(options.ModelCopyPath(originalSource));
                    if (string.Equals(copy, fullSource, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("配套模型不能覆盖原零件，请另选输出目录。");
                    if (_application.IGetOpenDocumentByName2(copy) != null)
                        throw new InvalidOperationException("配套模型正在打开，请关闭该副本后再导出。");
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(fullSource, copy, options.OverwriteExisting);
                    fullSource = copy;
                }
                partModel = _application.IGetOpenDocumentByName2(fullSource);
                if (partModel == null)
                {
                    int openErrors = 0;
                    int openWarnings = 0;
                    int flags = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (options.CopyPartModel ? 0 : (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly);
                    partModel = _application.OpenDoc6(fullSource, isAssembly ? (int)swDocumentTypes_e.swDocASSEMBLY : (int)swDocumentTypes_e.swDocPART, flags, string.Empty, ref openErrors, ref openWarnings);
                    openedPart = partModel != null;
                    if (partModel == null)
                        return ExportResult.Failed(fullSource, "SOLIDWORKS 无法以只读方式打开零件。错误码：" + openErrors);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (options.Category == ProductionCategory.木板件 && !options.DryRunOnly)
                {
                    if (new HoleAttributeManager(_application).Enumerate(partModel).Any())
                        throw new InvalidOperationException("木板带有孔标签，暂不自动旋转副本；请先使用通用模式核对标注。");
                    WoodDrawingPreparation.AlignCopy(partModel);
                }
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

                if (options.CopyPartModel)
                {
                    var properties = partModel.Extension.CustomPropertyManager[string.Empty];
                    properties.Add3("图样名称", (int)swCustomInfoType_e.swCustomInfoText, options.OutputName(originalSource), (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                    properties.Add3("图样代号", (int)swCustomInfoType_e.swCustomInfoText, options.OutputName(originalSource), (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                    int errors = 0, warnings = 0;
                    if (!partModel.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0)
                        throw new IOException("配套模型保存失败，错误码：" + errors);
                }

                drawingModel = _application.NewDocument(template, ToPaperSize(plan.Paper.Choice), plan.Paper.Width, plan.Paper.Height) as IModelDoc2;
                if (!(drawingModel is IDrawingDoc drawing))
                    return ExportResult.Failed(fullSource, "创建工程图文档失败。");
                drawingModel.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swViewDisplayHideAllTypes, true);

                var sheet = drawing.GetCurrentSheet() as ISheet;
                if (options.Category == ProductionCategory.木板件 && sheet != null)
                    if (!drawing.SetupSheet6(sheet.GetName(), (int)swDwgPaperSizes_e.swDwgPapersUserDefined, (int)swDwgTemplates_e.swDwgTemplateNone, 1, 1, true, "", plan.Paper.Width, plan.Paper.Height, "", true, 0,0,0,0,0,0))
                        throw new InvalidOperationException("木板自定义图纸设置失败。");
                double scaleNumerator = plan.Scale < 1 ? 1 : plan.Scale;
                double scaleDenominator = plan.Scale < 1 ? 1 / plan.Scale : 1;
                if (sheet == null || !sheet.SetScale(scaleNumerator, scaleDenominator, false, false))
                    throw new InvalidOperationException("无法设置工程图图纸比例。");

                var availableViews = ((Array)partModel.GetModelViewNames()).Cast<string>().ToArray();
                var productionViews = new List<IView>();
                IView firstModelView = null;
                foreach (PlannedView planned in plan.Views)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string modelViewName = ModelViewNames.Resolve(planned.ModelViewName, availableViews);
                    IView view = drawing.CreateDrawViewFromModelView3(fullSource, modelViewName, planned.CenterX, planned.CenterY, 0.0);
                    if (view == null)
                        throw new InvalidOperationException("无法创建模型视图：" + planned.ModelViewName);
                    if (firstModelView == null) firstModelView = view;
                    view.UseSheetScale = 0;
                    view.ScaleDecimal = plan.Scale;
                    if (options.Category == ProductionCategory.木板件)
                        view.SetDisplayMode3(false, (int)swDisplayMode_e.swHIDDEN_GREYED, false, false);
                    if (planned.ModelViewName != "*Isometric") productionViews.Add(view);
                }
                // SW automatic view insertion can alter the sheet scale after its initial assignment.
                if (!sheet.SetScale(scaleNumerator, scaleDenominator, false, false))
                    throw new InvalidOperationException("无法保持指定的图纸比例。");
                drawingModel.EditRebuild3();

                if (isAssembly && options.Category == ProductionCategory.焊接)
                {
                    var bom = firstModelView.InsertBomTable4(false, plan.Paper.Width - .30, plan.Paper.Height - .04,
                        (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft,
                        (int)swBomType_e.swBomType_PartsOnly, partModel.ConfigurationManager.ActiveConfiguration.Name, "", false, 0, false);
                    if (bom == null) throw new InvalidOperationException("焊接零件明细表生成失败。");
                    WeldDrawingNotes.CreateLabels(drawingModel, firstModelView);
                }

                int annotations = 0;
                if (options.AddHoleAnnotations && !isAssembly)
                {
                    var attributes = new HoleAttributeManager(_application);
                    var resolver = new DrawingHoleResolver(attributes, new PersistentReferenceManager(new GeometricFaceFallbackResolver()));
                    var matches = resolver.Resolve(drawingModel);
                    if (options.Category != ProductionCategory.通用) matches = ProductionDrawingNotes.GroupIdenticalLabels(matches);
                    annotations = new HoleAnnotationGenerator().CreateNotes(drawingModel, matches);
                    if (options.Category == ProductionCategory.木板件)
                        annotations += UnmarkedHoleNotes.Create(drawingModel, partModel, attributes);
                    drawingModel.EditRebuild3();
                }

                int dimensions = 0;
                if (options.Category != ProductionCategory.通用)
                {
                    if (!isAssembly) foreach (var view in productionViews) dimensions += ProductionDrawingNotes.AddDimensions(drawingModel, view);
                    ProductionDrawingNotes.AddSummary(drawingModel, options, originalSource, plan, annotations, ManufacturingMeasurements.Describe(partModel,options.Category));
                    drawingModel.EditRebuild3();
                }
                result = new ExportResult { SourcePath = originalSource, Success = true, Plan = plan, DryRunReport = dryRunReport, AnnotationCount = annotations };
                foreach (string path in requestedOutputs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase))
                    { result.OutputPaths.Add(path); continue; }
                    if (!options.OverwriteExisting && File.Exists(path)) throw new IOException("输出文件已存在：" + path);
                    if (string.Equals(Path.GetExtension(path), ".step", StringComparison.OrdinalIgnoreCase))
                    { Directory.CreateDirectory(Path.GetDirectoryName(path)); partModel.ClearSelection2(true); Save(partModel, path, null); result.OutputPaths.Add(path); continue; }
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
                result.Message = string.Format("{0}，比例 {1:0.###}:1，孔标注 {2} 个，参考尺寸 {3} 个。", plan.Paper.Name, plan.Scale, annotations, dimensions);
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
            // true keeps SOLIDWORKS system units (meters), which is what the planner expects.
            object rawBox;
            if (model is IPartDoc part) rawBox = part.GetPartBox(true);
            else if (model is IAssemblyDoc assembly) rawBox = assembly.GetBox(0);
            else throw new InvalidOperationException("文档不是零件或装配体。");
            var box = (rawBox as double[]) ?? ToArray(rawBox);
            if (box == null || box.Length < 6) throw new InvalidOperationException("无法读取零件包围盒。");
            return new ModelBounds { MinX = box[0], MinY = box[1], MinZ = box[2], MaxX = box[3], MaxY = box[4], MaxZ = box[5] };
        }

        private string ResolveTemplate(BatchExportOptions options, PaperDefinition paper)
        {
            if (!string.IsNullOrWhiteSpace(options.DrawingTemplatePath))
            {
                string path = Path.GetFullPath(options.DrawingTemplatePath);
                if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), "^gb_a[0-4]\\.drwdot$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && paper.Choice != PaperChoice.Custom)
                {
                    string matching = Path.Combine(Path.GetDirectoryName(path), "gb_" + paper.Name.ToLowerInvariant() + ".drwdot");
                    if (File.Exists(matching)) return matching;
                }
                return path;
            }
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
                case PaperChoice.Custom: return (int)swDwgPaperSizes_e.swDwgPapersUserDefined;
                default: throw new ArgumentOutOfRangeException(nameof(choice));
            }
        }

        private void Save(IModelDoc2 model, string path, object exportData)
        {
            int errors = 0;
            int warnings = 0;
            if (_application.ActivateDoc3(model.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors) == null)
                throw new InvalidOperationException("导出前无法激活文档：" + model.GetTitle());
            errors = 0;
            model.ClearSelection2(true);
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
