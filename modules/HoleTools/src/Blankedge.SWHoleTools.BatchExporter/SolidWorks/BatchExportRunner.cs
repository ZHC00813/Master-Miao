using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using Blankedge.SWHoleTools.BatchExporter.Model;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    public sealed class BatchExportRunner
    {
        public void Run(IList<string> files, BatchExportOptions options, CancellationToken cancellationToken, Action<BatchProgress> progress)
        {
            Run(files.Select(path => new DrawingExportJob { SourcePath = path, ScaleText = options.ScaleMode == ScaleChoice.AutoFit ? "自动" : options.FixedScale.ToString("R", CultureInfo.InvariantCulture) }).ToList(), options, cancellationToken, progress);
        }

        public void Run(IList<DrawingExportJob> jobs, BatchExportOptions options, CancellationToken cancellationToken, Action<BatchProgress> progress)
        {
            jobs = jobs.Where(job => job.Included).Select(job => job.Snapshot()).ToList();
            options = options.Snapshot();
            var errors = DrawingExportJob.ValidateBatch(jobs, options);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(System.Environment.NewLine, errors));
            using (SolidWorksSession session = SolidWorksSession.Connect(options.ConnectionMode, cancellationToken))
            {
                IModelDoc2 originalActive = null;
                string originalTitle = string.Empty;
                try
                {
                    originalActive = session.Application.IActiveDoc2;
                    originalTitle = originalActive?.GetTitle() ?? string.Empty;
                }
                catch { }
                var engine = new DrawingExportEngine(session.Application);
                try
                {
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        ExportResult result = engine.Export(jobs[i].SourcePath, jobs[i].CreateOptions(options), cancellationToken);
                        progress?.Invoke(new BatchProgress
                        {
                            Completed = i + 1,
                            Total = jobs.Count,
                            Result = result,
                            Message = result.Message
                        });
                    }
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(originalTitle))
                    {
                        try
                        {
                            int activateErrors = 0;
                            session.Application.ActivateDoc3(originalTitle, true, 0, ref activateErrors);
                        }
                        catch { }
                    }
                }
            }
        }
    }
}
