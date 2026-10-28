using JobTracker.Modules.JobAnalysis.Application.BatchImport;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed class BatchGroupingResolver(OllamaClient client, IScreenshotDiscovery discovery, ImagePreparationService preparation) : IBatchGroupingResolver
{
    public async Task<IReadOnlyList<ResolvedScreenshotGroup>> ResolveAsync(IReadOnlyList<BatchScreenshot> images, OllamaOptions options,
        BatchImportOptions limits, Action<BatchModelCall> onCall, CancellationToken ct)
    {
        if (images.Count is < 1 or > 8 || images.Count > limits.MaxImagesInMemory) throw new ArgumentException("Janela visual fora dos limites.");
        var encoded = new List<string>();
        long bytes = 0, pixels = 0;
        foreach (var image in images)
        {
            var input = await discovery.LoadVerifiedAsync(image, ct);
            var prepared = preparation.Prepare(input, options, ct);
            bytes += prepared.Bytes.Length; pixels += prepared.Pixels;
            if (bytes > options.MaxBatchImageBytes || pixels > options.MaxBatchPixels)
                throw new ArgumentException("A janela excede os limites de bytes ou pixels. Reduza a janela visual ou ajuste os limites Ollama.");
            encoded.Add(Convert.ToBase64String(prepared.Bytes.Span));
        }
        var result = await client.GroupScreenshotsAsync(options, encoded, limits.KeepAliveSeconds, onCall, ct);
        Validate(result, images.Count);
        return result.Partitions.Select(p => new ResolvedScreenshotGroup(p.ImageIndices, p.Company ?? "", p.Title ?? "", p.Confident,
            p.Data.MultipleJobs || IsEmpty(p.Data) ? null : OllamaResultMapper.Map([p.Data], options.Model, p.ImageIndices.Length, ct))).ToArray();
    }

    private static bool IsEmpty(OllamaJobData data) => new[] { data.Company, data.Title, data.Description, data.Location, data.WorkMode,
        data.Seniority, data.EmploymentType, data.Salary, data.Url }.All(string.IsNullOrWhiteSpace) && data.Technologies.Length == 0;

    internal static void Validate(OllamaGroupingData result, int count)
    {
        if (result.Partitions is null || result.Partitions.Length is < 1 or > 8 ||
            result.Partitions.Any(p => p is null || p.ImageIndices is null || p.ImageIndices.Length is < 1 or > 8 ||
                p.Company?.Length > 200 || p.Title?.Length > 300 || p.Data is null) ||
            !result.Partitions.SelectMany(p => p.ImageIndices).SequenceEqual(Enumerable.Range(0, count))) throw OllamaClient.InvalidResponse();
        foreach (var part in result.Partitions)
        {
            // Still validate field shapes for ambiguous/multiple-job partitions, without combining them.
            OllamaResultMapper.Validate(part.Data with { MultipleJobs = false });
            if (part.Confident && part.Data.MultipleJobs) throw OllamaClient.InvalidResponse();
            if (Conflicts(part.Company, part.Data.Company) || Conflicts(part.Title, part.Data.Title)) throw OllamaClient.InvalidResponse();
        }
    }
    private static bool Conflicts(string? first, string? second) => !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
        JobTextKey.Normalize(first) != JobTextKey.Normalize(second);
}
