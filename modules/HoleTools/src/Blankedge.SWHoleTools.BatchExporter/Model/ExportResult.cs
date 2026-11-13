using System;
using System.Collections.Generic;

namespace Blankedge.SWHoleTools.BatchExporter.Model
{
    public sealed class ExportResult
    {
        public string SourcePath { get; set; }
        public bool Success { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; }
        public DrawingLayoutPlan Plan { get; set; }
        public PartDryRunReport DryRunReport { get; set; }
        public int AnnotationCount { get; set; }
        public IList<string> OutputPaths { get; } = new List<string>();

        public static ExportResult Failed(string source, string message) => new ExportResult { SourcePath = source, Message = message };
        public static ExportResult Skip(string source, string message) => new ExportResult { SourcePath = source, Skipped = true, Message = message };
    }

    public sealed class BatchProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public string Message { get; set; }
        public ExportResult Result { get; set; }
    }
}
