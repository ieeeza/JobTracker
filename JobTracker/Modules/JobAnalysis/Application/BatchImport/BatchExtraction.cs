using System.Diagnostics;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;

namespace JobTracker.Modules.JobAnalysis.Application.BatchImport;

public sealed class BatchExtraction(IBatchJobInterpreter interpreter)
{
    public async Task ExtractAsync(ImportBatch batch, DetectedJobGroup group, BatchImportOptions limits, OllamaOptions ollama, CancellationToken ct)
    {
        var images = batch.ImagesFor(group).ToArray();
        if (images.Length == 0 || images.Any(i => i.Hash.Length == 0 || i.Error is not null))
            throw new InvalidOperationException("Há capturas inválidas no grupo. Corrija a origem antes de analisar.");
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await interpreter.InterpretAsync(new(images, group.ForceRefresh), ollama, limits, call => Record(batch, call), ct);
            if (group.Confidence != GroupingConfidence.ManuallyReviewed &&
                (Conflict(group.IdentityCompany, result.Content.Company) || Conflict(group.IdentityTitle, result.Content.Title)))
            { group.Confidence = GroupingConfidence.Ambiguous; group.BoundaryAmbiguous = true; group.Reason = "A extração diverge da identidade identificada no agrupamento."; }
            group.Content = result.Content; group.Url = result.Url;
            group.UsedAi = true; group.ExtractionCompleted = true; group.ForceRefresh = false;
            group.InferenceImageIds = images.Select(i => i.Id).ToList();
            group.Evidence = result.Evidence;
            foreach (var image in images) image.Analyzed = true;
        }
        finally { batch.Metrics.InferenceMilliseconds += watch.Elapsed.TotalMilliseconds; }
    }
    private static bool Conflict(string first, string second) => first.Length > 0 && second.Length > 0 && JobTextKey.Normalize(first) != JobTextKey.Normalize(second);
    public static bool IsSafe(DetectedJobGroup group, IReadOnlyList<BatchScreenshot> images) =>
        group.Confidence != GroupingConfidence.Ambiguous && !group.BoundaryAmbiguous && group.UsedAi && group.ExtractionCompleted &&
        group.Content is { } content && !string.IsNullOrWhiteSpace(content.Title) && !string.IsNullOrWhiteSpace(content.Company) &&
        !string.IsNullOrWhiteSpace(content.Description) && images.Count > 0 && images.All(i => i.Error is null && i.Hash.Length == 64);

    internal static void Record(ImportBatch batch, BatchModelCall call)
    {
        if (call.CacheHit) { batch.Metrics.CacheHits++; return; }
        if (call.Started) batch.Metrics.ModelCalls++;
        else
        {
            batch.Metrics.OllamaTotalNanoseconds += call.TotalNanoseconds;
            batch.Metrics.OllamaLoadNanoseconds += call.LoadNanoseconds;
            batch.Metrics.OllamaEvaluationNanoseconds += call.EvaluationNanoseconds;
        }
    }
}
