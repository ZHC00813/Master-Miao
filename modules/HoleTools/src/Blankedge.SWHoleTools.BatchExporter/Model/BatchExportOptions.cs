using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace Blankedge.SWHoleTools.BatchExporter.Model
{
    public enum ProductionCategory { 通用, 钣金切割件, 钣金折弯件, 管件, 木板件, 轴件, 焊接 }
    public enum PaperChoice { Auto, A4, A3, A2, A1, A0, Custom }
    public enum ScaleChoice { AutoFit, Fixed }
    public enum SolidWorksConnectionMode { ExistingOnly, StartHidden }

    [DataContract]
    public sealed class ViewPreset
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public List<string> ModelViewNames { get; set; } = new List<string>();

        public override string ToString() => Name ?? "未命名视图方案";

        public static IList<ViewPreset> Defaults()
        {
            return new[]
            {
                new ViewPreset { Name = "三视图 + 等轴测", ModelViewNames = new List<string> { "*Front", "*Top", "*Left", "*Isometric" } },
                new ViewPreset { Name = "三视图", ModelViewNames = new List<string> { "*Front", "*Top", "*Left" } },
                new ViewPreset { Name = "正视图 + 等轴测", ModelViewNames = new List<string> { "*Front", "*Isometric" } }
            };
        }
    }

    [DataContract]
    public sealed class BatchExportOptions
    {
        [DataMember(Order = 1)] public string OutputDirectory { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string DrawingTemplatePath { get; set; } = string.Empty;
        [DataMember(Order = 3)] public PaperChoice Paper { get; set; } = PaperChoice.Auto;
        [DataMember(Order = 4)] public ScaleChoice ScaleMode { get; set; } = ScaleChoice.AutoFit;
        [DataMember(Order = 5)] public double FixedScale { get; set; } = 1.0;
        [DataMember(Order = 6)] public ViewPreset Views { get; set; } = (ViewPreset)ViewPreset.Defaults()[0];
        [DataMember(Order = 7)] public bool SaveDrawing { get; set; } = true;
        [DataMember(Order = 8)] public bool SavePdf { get; set; }
        [DataMember(Order = 9)] public bool AddHoleAnnotations { get; set; } = true;
        [DataMember(Order = 10)] public bool OverwriteExisting { get; set; }
        [DataMember(Order = 11)] public SolidWorksConnectionMode ConnectionMode { get; set; } = SolidWorksConnectionMode.ExistingOnly;
        [DataMember(Order = 12)] public bool DryRunOnly { get; set; }
        [DataMember(Order = 13)] public bool SaveDwg { get; set; }
        [DataMember(Order = 14)] public bool SaveDxf { get; set; }
        [DataMember(Order = 15)] public string NamePrefix { get; set; } = string.Empty;

        [DataMember(Order = 16)] public ProductionCategory Category { get; set; }
        [DataMember(Order = 17)] public string MaterialText { get; set; } = string.Empty;
        [DataMember(Order = 18)] public string FinishText { get; set; } = string.Empty;
        [DataMember(Order = 19)] public string ManufacturingNotes { get; set; } = string.Empty;
        [DataMember(Order = 20)] public bool SaveStep { get; set; }
        [DataMember(Order = 21)] public bool CopyPartModel { get; set; }
        public string PackageDirectory => Category == ProductionCategory.通用 ? OutputDirectory : Path.Combine(OutputDirectory, Category + "生产资料");
        public string OutputName(string source) => (NamePrefix ?? string.Empty) + Path.GetFileNameWithoutExtension(source);
        public string ModelCopyPath(string source) => Path.Combine(PackageDirectory, "配套模型", OutputName(source) + "_生产模型.SLDPRT");
        public string DrawingFolder => Category == ProductionCategory.焊接 ? "注释图" : "SolidWorks工程图";
        public string PdfFolder => Category == ProductionCategory.焊接 ? "注释图PDF" : "PDF";
        public string StepFolder => Category == ProductionCategory.焊接 ? "STEP" : Category == ProductionCategory.钣金切割件 ? "STEP模型文件" : "STEP模型";

        public BatchExportOptions Snapshot()
        {
            var copy = (BatchExportOptions)MemberwiseClone();
            copy.Views = new ViewPreset { Name = Views?.Name, ModelViewNames = new List<string>(Views?.ModelViewNames ?? new List<string>()) };
            return copy;
        }

        public IList<string> OutputPaths(string source)
        {
            if (DryRunOnly) return new List<string>();
            string name = (NamePrefix ?? string.Empty) + Path.GetFileNameWithoutExtension(source);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" "))
                throw new InvalidOperationException("导出文件名无效：" + name);
            var paths = new List<string>();
            if (SaveDrawing || Category != ProductionCategory.通用) paths.Add(Path.Combine(PackageDirectory, DrawingFolder, name + ".SLDDRW"));
            if (SavePdf || Category != ProductionCategory.通用) paths.Add(Path.Combine(PackageDirectory, PdfFolder, name + ".pdf"));
            if (SaveDwg || Category != ProductionCategory.通用) paths.Add(Path.Combine(PackageDirectory, "DWG", name + ".dwg"));
            if (SaveDxf) paths.Add(Path.Combine(PackageDirectory, "DXF", name + ".dxf"));
            if (SaveStep) paths.Add(Path.Combine(PackageDirectory, StepFolder, name + ".STEP"));
            if (CopyPartModel) paths.Add(ModelCopyPath(source));
            return paths;
        }

        public IList<string> Validate()
        {
            var errors = new List<string>();
            if (!DryRunOnly && string.IsNullOrWhiteSpace(OutputDirectory)) errors.Add("请选择输出目录。");
            if (!string.IsNullOrWhiteSpace(DrawingTemplatePath) && !File.Exists(DrawingTemplatePath)) errors.Add("指定的工程图模板不存在。");
            if (ScaleMode == ScaleChoice.Fixed && (FixedScale <= 0.0 || double.IsNaN(FixedScale) || double.IsInfinity(FixedScale))) errors.Add("固定比例必须大于 0。");
            if (!DryRunOnly && Category == ProductionCategory.通用 && !SaveDrawing && !SavePdf && !SaveDwg && !SaveDxf && !SaveStep && !CopyPartModel) errors.Add("至少选择一种输出格式。");
            if (Category == ProductionCategory.焊接 && CopyPartModel) errors.Add("焊接装配体不能复制为单个零件，请取消配套 SLDPRT。");
            if (Category == ProductionCategory.木板件 && !CopyPartModel) errors.Add("木板生产出图需要配套模型副本，以保留原装配方向。");
            if (Paper == PaperChoice.Custom && Category != ProductionCategory.木板件) errors.Add("自定义纸张目前用于木板件 1:1 出图。");
            if ((NamePrefix ?? string.Empty).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) errors.Add("前缀不能包含路径分隔符或非法文件名字符。");
            if (Views?.ModelViewNames == null || Views.ModelViewNames.Count == 0) errors.Add("视图方案不能为空。");
            return errors;
        }
    }
}
