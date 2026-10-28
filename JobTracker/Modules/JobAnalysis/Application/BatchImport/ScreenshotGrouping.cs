using System.Diagnostics;
using System.IO;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;

namespace JobTracker.Modules.JobAnalysis.Application.BatchImport;

public sealed class ScreenshotGrouping(IBatchGroupingResolver resolver, IImportBatchStore store)
{
    public const int Version = 2;
    public async Task GroupAsync(ImportBatch batch, BatchImportOptions limits, OllamaOptions options,
        Action<string, int, int> report, CancellationToken ct)
    {
        if (batch.Groups.Count > 0)
        {
            if (batch.AnalysisVersion != Version)
            {
                // Historical reports remain intact. Old unfinished results cannot become new AI imports.
                foreach (var group in batch.Groups.Where(g => g.Status is ImportItemStatus.Pending or ImportItemStatus.Processing or ImportItemStatus.Failed))
                {
                    group.Status = ImportItemStatus.NeedsReview; group.ExtractionCompleted = false;
                    group.Confidence = GroupingConfidence.Ambiguous;
                    group.Reason = "Lote anterior à análise exclusivamente visual. Confira a composição e reanalise este grupo.";
                }
                batch.AnalysisVersion = Version;
            }
            return;
        }
        int size = Math.Min(limits.MaxImagesInMemory, options.BatchSize);
        if (size < 2) throw new ArgumentException("O agrupamento exige ao menos duas imagens por requisição. Ajuste o limite Ollama.");
        var ordered = batch.Images.OrderBy(i => i.Order).ToArray();
        var groups = new List<DetectedJobGroup>();
        var assignments = new Dictionary<Guid, DetectedJobGroup>();
        var candidates = new Dictionary<Guid, (Guid[] Images, JobImageAnalysisResult Analysis)>();
        var watch = Stopwatch.StartNew();
        try
        {
            int start = 0;
            while (start < ordered.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (ordered[start].Error is not null)
                {
                    if (groups.Count > 0) Uncertain(groups[^1], "Captura vizinha inválida; confira o limite da vaga.");
                    var invalid = NewGroup(groups);
                    invalid.Status = ImportItemStatus.Failed;
                    Uncertain(invalid, ordered[start].Error ?? "Imagem inválida.");
                    assignments[ordered[start].Id] = invalid;
                    start++; continue;
                }
                var window = ordered.Skip(start).Take(size).TakeWhile(i => i.Error is null).ToArray();
                report("Identificando vagas com Qwen3-VL", start, ordered.Length);
                IReadOnlyList<ResolvedScreenshotGroup> partitions;
                try
                {
                    partitions = await resolver.ResolveAsync(window, options, limits, call => BatchExtraction.Record(batch, call), ct);
                    foreach (var image in window) image.Analyzed = true;
                }
                catch (ImageAnalysisException ex) when (ex.Failure is ImageAnalysisFailure.Disconnected or ImageAnalysisFailure.ModelMissing or ImageAnalysisFailure.Timeout or ImageAnalysisFailure.ServerFailure)
                { throw; }
                catch (Exception ex) when (ex is ImageAnalysisException or ArgumentException or IOException or UnauthorizedAccessException)
                {
                    // Preserve the entire uncertain window for review, never fabricate per-image jobs.
                    partitions = [new(Enumerable.Range(0, window.Length).ToArray(), "", "", false, null)];
                    batch.Notices.Add($"Janela {start + 1}–{start + window.Length}: " +
                        (ex is IOException or UnauthorizedAccessException ? "Não foi possível ler as capturas de origem." : ex.Message));
                }
                foreach (var part in partitions)
                {
                    var images = part.ImageIndices.Select(index => window[index]).ToArray();
                    bool overlapping = assignments.TryGetValue(images[0].Id, out var previous);
                    var group = overlapping && previous is not null ? previous : NewGroup(groups);
                    if (Conflict(group.IdentityCompany, part.Company) || Conflict(group.IdentityTitle, part.Title))
                        Uncertain(group, "Identidades divergentes entre janelas visuais; separe ou revise as capturas.");
                    if (!part.Confident) Uncertain(group, "O modelo não confirmou os limites visuais desta vaga.");
                    if (group.IdentityCompany.Length == 0) group.IdentityCompany = part.Company;
                    if (group.IdentityTitle.Length == 0) group.IdentityTitle = part.Title;
                    foreach (var image in images) assignments[image.Id] = group;
                    var associated = ordered.Where(i => assignments.TryGetValue(i.Id, out var owner) && owner == group).Select(i => i.Id).ToArray();
                    if (part.Analysis is { } analysis && associated.SequenceEqual(images.Select(i => i.Id)))
                        candidates[group.Id] = (associated, analysis);
                    else if (candidates.TryGetValue(group.Id, out var prior) && !prior.Images.SequenceEqual(associated)) candidates.Remove(group.Id);
                    if (candidates.Values.Sum(c => (long)c.Analysis.ReviewText.Length) > 16000000)
                        throw new InvalidOperationException("As extrações excedem 16 milhões de caracteres. Divida a pasta em lotes menores; as respostas concluídas permanecem no cache.");
                }
                // Persist metrics and cache-backed progress before the next expensive request.
                await store.SaveAsync(batch, ct);
                int end = start + window.Length;
                if (end == ordered.Length) break;
                start = ordered[end].Error is null && window.Length > 1 ? end - 1 : end;
            }
            // Uncertain boundaries affect both sides; a partial adjacent job must not be auto-saved.
            var uncertain = groups.Select((g, i) => (g, i)).Where(x => x.g.BoundaryAmbiguous).Select(x => x.i).ToArray();
            foreach (int index in uncertain)
            {
                if (index > 0) Uncertain(groups[index - 1], "Limite compartilhado com um grupo incerto; confira as imagens.");
                if (index + 1 < groups.Count) Uncertain(groups[index + 1], "Limite compartilhado com um grupo incerto; confira as imagens.");
            }
            ct.ThrowIfCancellationRequested();
            foreach (var group in groups)
            {
                var images = ordered.Where(i => assignments[i.Id] == group).ToArray();
                group.Fingerprint = ImportBatch.Hash(images.Select(i => i.Hash));
                if (candidates.TryGetValue(group.Id, out var candidate) && candidate.Images.SequenceEqual(images.Select(i => i.Id)))
                {
                    var analysis = candidate.Analysis;
                    group.Content = analysis.Content; group.Url = analysis.Url; group.Evidence = analysis.Evidence;
                    group.ExtractionCompleted = true; group.UsedAi = true;
                    group.InferenceImageIds = images.Select(i => i.Id).ToList();
                }
                if (group.BoundaryAmbiguous && group.Status != ImportItemStatus.Failed) group.Status = ImportItemStatus.NeedsReview;
            }
            // Publish a complete partition atomically. Cancellation never leaves half a grouping.
            foreach (var image in ordered) image.GroupId = assignments[image.Id].Id;
            batch.Groups = groups; batch.AnalysisVersion = Version;
        }
        finally { batch.Metrics.GroupingMilliseconds += watch.Elapsed.TotalMilliseconds; }
    }

    private static DetectedJobGroup NewGroup(List<DetectedJobGroup> groups)
    {
        var group = new DetectedJobGroup { Order = groups.Count, Confidence = GroupingConfidence.High, Reason = "Limites estimados pelo Qwen3-VL." };
        groups.Add(group); return group;
    }
    private static bool Conflict(string first, string second) => first.Length > 0 && second.Length > 0 && JobTextKey.Normalize(first) != JobTextKey.Normalize(second);
    private static void Uncertain(DetectedJobGroup group, string reason)
    { group.Confidence = GroupingConfidence.Ambiguous; group.BoundaryAmbiguous = true; group.Reason = reason; }
}
