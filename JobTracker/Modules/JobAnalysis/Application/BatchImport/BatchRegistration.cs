using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;

namespace JobTracker.Modules.JobAnalysis.Application.BatchImport;

public sealed class BatchRegistration(TrackingService tracking)
{
    public async Task<bool> RecoverAsync(ImportBatch batch, DetectedJobGroup group, CancellationToken ct)
    {
        var jobs = await tracking.ListAsync(ct);
        var existing = jobs.FirstOrDefault(j => j.BatchImportKey == group.Fingerprint);
        if (existing is not null)
        {
            group.JobId = existing.Id; group.Content = existing.Content;
            group.Url = existing.Url; group.Capture = existing.OcrCapture; group.ExtractionCompleted = true; group.ExistingJobId = null;
            group.Status = ImportItemStatus.Imported; group.Reused = true;
            group.Reason = "Origem já persistida; vínculo recuperado sem nova inserção.";
            return true;
        }
        var hashes = batch.ImagesFor(group).Select(i => i.Hash).Where(h => h.Length > 0).ToHashSet(StringComparer.Ordinal);
        existing = jobs.FirstOrDefault(j => j.BatchImageHashes.Split('\n').Any(hashes.Contains));
        if (existing is not null)
        {
            group.ExistingJobId = existing.Id; group.Status = ImportItemStatus.NeedsReview;
            group.Reason = "Uma captura já está vinculada a outra vaga. Confira o agrupamento e o registro existente.";
            return true;
        }
        return false;
    }

    public async Task SaveAsync(ImportBatch batch, DetectedJobGroup group, bool reviewed, bool allowSimilar, CancellationToken ct)
    {
        if (group.Content is not { } content)
        {
            if (reviewed) throw new InvalidOperationException("Preencha os dados antes de salvar a pendência.");
            group.Status = ImportItemStatus.NeedsReview;
            group.Reason = "Nenhum dado suficiente foi reconhecido; confira as capturas e preencha a pendência.";
            return;
        }
        if (reviewed && group.JobId is { } id)
        {
            var stored = (await tracking.ListAsync(ct)).SingleOrDefault(j => j.Id == id)
                ?? throw new InvalidOperationException("A vaga salva não foi encontrada.");
            await tracking.EditAsync(stored, content, stored.Status, stored.AppliedAt, group.Url == stored.Url ? stored.CanonicalUrl : null,
                Metadata(batch, group), ct, group.Capture, group.Fingerprint, batch.ImagesFor(group).Select(i => i.Hash).ToArray(), url: group.Url);
            group.Status = ImportItemStatus.Imported; group.Reason = "Dados revisados e atualizados no mesmo registro.";
            group.ExistingJobId = null;
            return;
        }
        if (!reviewed && !BatchExtraction.IsSafe(group, batch.ImagesFor(group).ToArray()))
        { group.Status = ImportItemStatus.NeedsReview; group.Reason = "Agrupamento, identidade ou evidência visual insuficiente para salvar automaticamente. " + group.Reason; return; }
        var matches = await tracking.FindDuplicatesAsync(group.Url, content, group.Capture, null, ct);
        var confirmed = matches.FirstOrDefault(m => m.Kind == DuplicateKind.Confirmed);
        if (confirmed is not null)
        { group.Status = ImportItemStatus.Duplicate; group.ExistingJobId = confirmed.Job.Id; group.Reason = confirmed.Reason; return; }
        if (!allowSimilar && matches.FirstOrDefault() is { } possible)
        { group.Status = ImportItemStatus.NeedsReview; group.ExistingJobId = possible.Job.Id; group.Reason = possible.Reason; return; }
        group.JobId = await tracking.CreateAsync(group.Url, content, null, Metadata(batch, group), ApplicationStatus.Saved,
            null, allowSimilar, ct, group.Capture, "Pasta de screenshots", group.Fingerprint,
            batch.ImagesFor(group).Select(i => i.Hash).ToArray());
        group.Status = ImportItemStatus.Imported; group.Reused = false; group.ExistingJobId = null; group.Reason = "Vaga salva no SQLite. Candidatura não enviada.";
    }

    private static string Metadata(ImportBatch batch, DetectedJobGroup group) =>
        $"Importação por pasta • lote {batch.Id} • grupo {group.Id} • Qwen3-VL local • {group.Confidence} • hash {group.Fingerprint}";
}
