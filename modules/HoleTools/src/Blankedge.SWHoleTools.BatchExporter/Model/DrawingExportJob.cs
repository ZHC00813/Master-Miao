using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Blankedge.SWHoleTools.BatchExporter.Model
{
    public sealed class DrawingExportJob
    {
        public bool Included { get; set; } = true;
        public string SourcePath { get; set; }
        public string OriginalName => Path.GetFileNameWithoutExtension(SourcePath);
        public string ExportName { get; set; }
        public string ScaleText { get; set; } = "1:1";

        public BatchExportOptions CreateOptions(BatchExportOptions common)
        {
            var result = common.Snapshot();
            ApplyScale(result, ScaleText);
            return result;
        }

        public DrawingExportJob Snapshot() => (DrawingExportJob)MemberwiseClone();

        public static void ApplyScale(BatchExportOptions options, string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text == "自动" || string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
            {
                options.ScaleMode = ScaleChoice.AutoFit;
                return;
            }
            string[] parts = text.Replace('：', ':').Split(':');
            double numerator, denominator = 1;
            if (parts.Length > 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out numerator) ||
                (parts.Length == 2 && !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out denominator)) ||
                double.IsNaN(numerator) || double.IsInfinity(numerator) || double.IsNaN(denominator) || double.IsInfinity(denominator) || numerator <= 0 || denominator <= 0)
                throw new InvalidOperationException("无效比例：" + text + "。请输入 1:1、1:2、2:1 或 自动。");
            double value = numerator / denominator;
            if (value < 0.001 || value > 100) throw new InvalidOperationException("比例倍率必须在 0.001 到 100 之间。");
            options.ScaleMode = ScaleChoice.Fixed;
            options.FixedScale = value;
        }

        public static IList<string> ValidateBatch(IList<DrawingExportJob> jobs, BatchExportOptions common)
        {
            var errors = common.Validate().ToList();
            var included = jobs.Where(job => job.Included).ToList();
            if (included.Count == 0) errors.Add("请勾选至少一个零件。");
            var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var job in included)
            {
                try
                {
                    if (!File.Exists(job.SourcePath)) errors.Add("源文件不存在：" + job.SourcePath);
                    if (!PartFileDiscovery.IsSupported(job.SourcePath)) errors.Add("不是 SolidWorks 零件或装配体：" + job.SourcePath);
                    var options = job.CreateOptions(common);
                    foreach (string path in options.OutputPaths(job.SourcePath))
                        if (!outputs.Add(Path.GetFullPath(path))) errors.Add("导出重名：" + path + "。请分批导出或调整原零件名称。");
                }
                catch (Exception ex) { errors.Add(job.OriginalName + ": " + ex.Message); }
            }
            return errors;
        }
    }

    public static class PartFileDiscovery
    {
        public static bool IsSupported(string path) => string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(path), ".sldasm", StringComparison.OrdinalIgnoreCase);
        public static IList<string> Collect(IEnumerable<string> inputs, Action<string> warning)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string input in inputs ?? Enumerable.Empty<string>())
            {
                if (Directory.Exists(input)) Visit(input, result, warning);
                else if (File.Exists(input) && IsSupported(input)) result.Add(Path.GetFullPath(input));
                else warning?.Invoke("跳过不存在或不支持的路径：" + input);
            }
            return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }
        private static void Visit(string directory, HashSet<string> result, Action<string> warning)
        {
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { warning?.Invoke("跳过链接目录：" + directory); return; }
                foreach (string file in Directory.GetFiles(directory))
                    if (IsSupported(file)) result.Add(Path.GetFullPath(file));
                foreach (string child in Directory.GetDirectories(directory)) Visit(child, result, warning);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { warning?.Invoke("无法读取目录：" + directory + "；" + ex.Message); }
        }
    }
}
