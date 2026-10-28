namespace JobTracker.Modules.JobAnalysis.Application.BatchImport;

public sealed class BatchReviewService(IImportBatchStore store, BatchRegistration registration)
{
    public async Task SynchronizeJobAsync(JobTracker.Modules.JobApplications.Application.JobInfo job, CancellationToken ct)
    {
        foreach (var history in await store.HistoryAsync(ct))
        {
            var batch = await store.LoadAsync(history.Id, ct);
            var groups = batch.Groups.Where(g => g.JobId == job.Id && g.Status == ImportItemStatus.Imported).ToArray();
            if (groups.Length == 0) continue;
            foreach (var group in groups)
            { group.Content = job.Content; group.Url = job.Url; group.Capture = job.OcrCapture; group.Reason = "Dados sincronizados após edição no registro existente."; }
            await store.SaveAsync(batch, ct);
        }
    }
    public async Task MoveAsync(ImportBatch batch, Guid imageId, Guid targetId, CancellationToken ct)
    {
        var image = batch.Images.Single(i => i.Id == imageId);
        var target = batch.Groups.Single(g => g.Id == targetId);
        var source = batch.Groups.Single(g => g.Id == image.GroupId);
        if (source.Id == target.Id) throw new ArgumentException("Selecione um grupo de destino diferente.");
        if (batch.ImagesFor(source).Count() == 1 && source.JobId is not null)
            throw new InvalidOperationException("Mover a última captura deixaria um registro salvo sem origem. Una os grupos ou revise o registro existente.");
        image.GroupId = target.Id;
        Reset(batch, source); Reset(batch, target);
        if (!batch.ImagesFor(source).Any()) batch.Groups.Remove(source);
        Renumber(batch);
        await store.SaveAsync(batch, ct);
    }
    public async Task SplitAsync(ImportBatch batch, Guid groupId, Guid firstImageOfNextGroup, CancellationToken ct)
    {
        var group = batch.Groups.Single(g => g.Id == groupId);
        var images = batch.ImagesFor(group).ToArray();
        int index = Array.FindIndex(images, i => i.Id == firstImageOfNextGroup);
        if (index <= 0) throw new ArgumentException("Selecione a primeira imagem da segunda vaga, após a primeira imagem do grupo.");
        var next = new DetectedJobGroup();
        batch.Groups.Insert(batch.Groups.IndexOf(group) + 1, next);
        foreach (var image in images.Skip(index)) image.GroupId = next.Id;
        Reset(batch, group); Reset(batch, next); Renumber(batch);
        await store.SaveAsync(batch, ct);
    }
    public async Task MergeAsync(ImportBatch batch, Guid sourceId, Guid targetId, CancellationToken ct)
    {
        var source = batch.Groups.Single(g => g.Id == sourceId);
        var target = batch.Groups.Single(g => g.Id == targetId);
        if (source.Id == target.Id) throw new ArgumentException("Selecione dois grupos diferentes.");
        if (source.JobId is not null && target.JobId is not null && source.JobId != target.JobId)
            throw new InvalidOperationException("Os dois grupos já possuem registros salvos diferentes. A união não pode apagar ou substituir candidaturas existentes. Revise os registros vinculados.");
        target.JobId ??= source.JobId;
        foreach (var image in batch.ImagesFor(source)) image.GroupId = target.Id;
        batch.Groups.Remove(source); Reset(batch, target); Renumber(batch);
        await store.SaveAsync(batch, ct);
    }
    public async Task ReprocessAsync(ImportBatch batch, DetectedJobGroup group, CancellationToken ct)
    {
        group.Status = ImportItemStatus.Pending;
        group.ExtractionCompleted = false; group.ForceRefresh = true;
        group.Reason = "Nova inferência solicitada para este grupo.";
        foreach (var image in batch.ImagesFor(group).Where(i => i.Error is not null && i.Hash.Length > 0))
        { image.Error = null; image.Analyzed = false; }
        await store.SaveAsync(batch, ct);
    }
    public async Task SaveReviewedAsync(ImportBatch batch, DetectedJobGroup group, bool allowSimilar, CancellationToken ct)
    {
        if (group.Status == ImportItemStatus.Discarded) throw new InvalidOperationException("Reative a pendência por reprocessamento antes de salvar.");
        if (batch.ImagesFor(group).Any(i => i.Hash.Length == 0)) throw new InvalidOperationException("Remova do grupo os arquivos inválidos antes de cadastrar.");
        group.Confidence = GroupingConfidence.ManuallyReviewed;
        await registration.SaveAsync(batch, group, true, allowSimilar, ct);
        await store.SaveAsync(batch, CancellationToken.None);
    }
    public async Task DiscardAsync(ImportBatch batch, DetectedJobGroup group, CancellationToken ct)
    {
        EnsureDiscardable(group);
        group.Status = ImportItemStatus.Discarded; group.Reason = "Pendência descartada pelo usuário. Capturas e histórico preservados.";
        await store.SaveAsync(batch, ct);
    }

    private static void EnsureDiscardable(DetectedJobGroup group)
    {
        if (group.JobId is not null)
            throw new InvalidOperationException("Este grupo já possui registro salvo. A ação de descartar só se aplica a pendências; abra o registro para gerenciá-lo.");
    }
    private static void Reset(ImportBatch batch, DetectedJobGroup group)
    {
        group.Confidence = GroupingConfidence.ManuallyReviewed; group.Status = ImportItemStatus.Pending;
        group.BoundaryAmbiguous = false;
        group.Content = null; group.Capture = null; group.Url = ""; group.ExistingJobId = null;
        group.Reason = "Agrupamento corrigido manualmente; reprocese este grupo.";
        group.Fingerprint = ImportBatch.Hash(batch.ImagesFor(group).Select(i => i.Hash));
        group.ForceRefresh = false;
        group.InferenceImageIds.Clear(); group.UsedAi = false; group.ExtractionCompleted = false;
        group.IdentityCompany = ""; group.IdentityTitle = ""; group.Evidence = "";
    }
    private static void Renumber(ImportBatch batch)
    { for (int index = 0; index < batch.Groups.Count; index++) batch.Groups[index].Order = index; }
}
