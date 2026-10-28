using System.Text.Json;
using JobTracker.Modules.Settings.Application;

namespace JobTracker.Modules.JobAnalysis.Application.BatchImport;

public sealed class BatchReportExport(ITransferFiles files)
{
    public Task ExportAsync(ImportBatch batch, string path, CancellationToken ct)
    {
        var report = new
        {
            Version = 1,
            batch.Id,
            batch.StartedAtUtc,
            batch.CompletedAtUtc,
            Status = batch.Status.ToString(),
            batch.Folder,
            batch.Summary,
            batch.Metrics,
            batch.Notices,
            Groups = batch.Groups.OrderBy(g => g.Order).Select(g => new
            {
                g.Id,
                Status = g.Status.ToString(),
                Confidence = g.Confidence.ToString(),
                g.Reason,
                g.Content,
                g.Url,
                g.JobId,
                g.ExistingJobId,
                g.Evidence,
                Images = batch.ImagesFor(g).Select(i => new { i.Name, i.Path, i.Hash, i.Order, i.Error })
            })
        };
        return files.WriteAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
    }
}
