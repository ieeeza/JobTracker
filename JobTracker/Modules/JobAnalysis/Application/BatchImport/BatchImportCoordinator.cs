using System.Diagnostics;
using System.IO;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobApplications.Application;

namespace JobTracker.Modules.JobAnalysis.Application.BatchImport;

public sealed class BatchImportCoordinator(IImportBatchStore store, IScreenshotDiscovery discovery, IOllamaConnection connection, ScreenshotGrouping grouping, BatchExtraction extraction, BatchRegistration registration)
{
    public Task RunAsync(ImportBatch batch, BatchImportOptions limits, OllamaOptions ollama,
        IProgress<BatchProgress>? progress, CancellationToken ct, Guid? onlyGroup = null) => Task.Run(async () =>
    {
        limits.Validate();
        batch.Options = limits;
        ollama.Validate();
        var watch = Stopwatch.StartNew();
        double previousTime = batch.Metrics.TotalMilliseconds;
        batch.Status = ImportBatchStatus.Processing; batch.CompletedAtUtc = null;
        void Report(string stage, int completed, int total) => progress?.Report(new(stage, completed, total, batch.Groups.Count,
            batch.Groups.Count(g => g.Status == ImportItemStatus.Imported), batch.Groups.Count(g => g.Status == ImportItemStatus.NeedsReview))
        { Summary = batch.Summary, GroupLines = batch.Groups.Select(g => g.Display).ToArray() });
        try
        {
            await store.SaveAsync(batch, ct);
            string identity = await connection.AnalysisIdentityAsync(ollama, ct);
            if (batch.AnalysisIdentity.Length > 0 && batch.AnalysisIdentity != identity)
            {
                foreach (var pending in batch.Groups.Where(g => g.Status is ImportItemStatus.Pending or ImportItemStatus.Processing or ImportItemStatus.Failed))
                {
                    pending.ExtractionCompleted = false;
                    if (pending.Confidence != GroupingConfidence.ManuallyReviewed)
                    {
                        pending.Confidence = GroupingConfidence.Ambiguous; pending.Status = ImportItemStatus.NeedsReview;
                        pending.Reason = "Modelo ou configuração mudou. Confira o agrupamento e reanalise antes de salvar.";
                    }
                }
            }
            batch.AnalysisIdentity = identity;
            if (onlyGroup is null) await grouping.GroupAsync(batch, limits, ollama, Report, ct);
            await store.SaveAsync(batch, ct);
            var groups = batch.Groups.Where(g => onlyGroup is null || g.Id == onlyGroup).OrderBy(g => g.Order).ToArray();
            for (int index = 0; index < groups.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var group = groups[index];
                if (group.Status is ImportItemStatus.Imported or ImportItemStatus.Duplicate or ImportItemStatus.Discarded or ImportItemStatus.NeedsReview) continue;
                if (group.JobId is null && await registration.RecoverAsync(batch, group, ct))
                { await store.SaveAsync(batch, ct); continue; }
                group.Status = ImportItemStatus.Processing;
                await store.SaveAsync(batch, ct);
                Report($"Analisando a vaga {group.Order + 1}", index, groups.Length);
                try
                {
                    foreach (var image in batch.ImagesFor(group)) await discovery.VerifyAsync(image, ct);
                    if (!group.ExtractionCompleted)
                    {
                        await extraction.ExtractAsync(batch, group, limits, ollama, ct);
                        await store.SaveAsync(batch, ct);
                    }
                    if (group.JobId is not null)
                    { group.Status = ImportItemStatus.NeedsReview; group.Reason = "Reprocessamento concluído. Confira e salve para atualizar o registro vinculado."; }
                    else await registration.SaveAsync(batch, group, false, false, ct);
                }
                catch (ImageAnalysisException ex) when (ex.Failure is ImageAnalysisFailure.Disconnected or ImageAnalysisFailure.ModelMissing or ImageAnalysisFailure.Timeout or ImageAnalysisFailure.ServerFailure)
                { group.Status = ImportItemStatus.Failed; group.Reason = ex.Message; throw; }
                catch (Exception ex) when (ex is ImageAnalysisException or ArgumentException or IOException or InvalidOperationException or SimilarJobException)
                {
                    group.Status = ex is ImageAnalysisException { Failure: ImageAnalysisFailure.ConflictingJobs } ? ImportItemStatus.NeedsReview : ImportItemStatus.Failed;
                    group.Reason = ex is IOException ? "Falha ao ler screenshot. Confira o arquivo de origem e reprocese o grupo." : ex.Message;
                }
                // Commit progress independently of a cancellation that may arrive after the job commit.
                batch.Metrics.TotalMilliseconds = previousTime + watch.Elapsed.TotalMilliseconds;
                await store.SaveAsync(batch, CancellationToken.None);
                Report($"Vaga {group.Order + 1}: {group.Label}", index + 1, groups.Length);
            }
            batch.Status = ImportBatchStatus.Completed; batch.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { batch.Status = ImportBatchStatus.Cancelled; batch.CompletedAtUtc = DateTime.UtcNow; }
        catch
        { batch.Status = ImportBatchStatus.Failed; batch.CompletedAtUtc = DateTime.UtcNow; throw; }
        finally
        {
            foreach (var group in batch.Groups.Where(g => g.Status == ImportItemStatus.Processing)) group.Status = ImportItemStatus.Pending;
            batch.Metrics.TotalMilliseconds = previousTime + watch.Elapsed.TotalMilliseconds;
            await store.SaveAsync(batch, CancellationToken.None);
            Report(batch.Status == ImportBatchStatus.Cancelled ? "Importação cancelada; progresso preservado" : "Relatório atualizado", batch.Groups.Count, batch.Groups.Count);
        }
    }, CancellationToken.None);

}
