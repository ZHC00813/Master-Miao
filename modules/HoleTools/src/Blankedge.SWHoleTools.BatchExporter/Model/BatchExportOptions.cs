using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace Blankedge.SWHoleTools.BatchExporter.Model
{
    public enum PaperChoice { Auto, A4, A3, A2, A1, A0 }
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
                new ViewPreset { Name = "三视图 + 等轴测", ModelViewNames = new List<string> { "*Front", "*Top", "*Right", "*Isometric" } },
                new ViewPreset { Name = "三视图", ModelViewNames = new List<string> { "*Front", "*Top", "*Right" } },
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
            if (SaveDrawing) paths.Add(Path.Combine(OutputDirectory, "SolidWorks工程图", name + ".SLDDRW"));
            if (SavePdf) paths.Add(Path.Combine(OutputDirectory, "PDF", name + ".pdf"));
            if (SaveDwg) paths.Add(Path.Combine(OutputDirectory, "DWG", name + ".dwg"));
            if (SaveDxf) paths.Add(Path.Combine(OutputDirectory, "DXF", name + ".dxf"));
            return paths;
        }

        public IList<string> Validate()
        {
            var errors = new List<string>();
            if (!DryRunOnly && string.IsNullOrWhiteSpace(OutputDirectory)) errors.Add("请选择输出目录。");
            if (!string.IsNullOrWhiteSpace(DrawingTemplatePath) && !File.Exists(DrawingTemplatePath)) errors.Add("指定的工程图模板不存在。");
            if (ScaleMode == ScaleChoice.Fixed && (FixedScale <= 0.0 || double.IsNaN(FixedScale) || double.IsInfinity(FixedScale))) errors.Add("固定比例必须大于 0。");
            if (!DryRunOnly && !SaveDrawing && !SavePdf && !SaveDwg && !SaveDxf) errors.Add("至少选择一种输出格式。");
            if ((NamePrefix ?? string.Empty).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) errors.Add("前缀不能包含路径分隔符或非法文件名字符。");
            if (Views?.ModelViewNames == null || Views.ModelViewNames.Count == 0) errors.Add("视图方案不能为空。");
            return errors;
        }
    }
}
