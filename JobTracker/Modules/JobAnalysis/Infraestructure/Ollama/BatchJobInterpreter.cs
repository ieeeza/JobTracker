using JobTracker.Modules.JobAnalysis.Application.BatchImport;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed class BatchJobInterpreter(OllamaClient client, IScreenshotDiscovery discovery, ImagePreparationService preprocessing) : IBatchJobInterpreter
{
    public async Task<JobImageAnalysisResult> InterpretAsync(BatchInferenceRequest request, OllamaOptions options,
        BatchImportOptions limits, Action<BatchModelCall> onCall, CancellationToken ct)
    {
        options.Validate(); limits.Validate();
        await client.VerifyAsync(options, ct).ConfigureAwait(false);
        var results = new List<OllamaJobData>();
        int position = 1;
        async Task ExtractAsync(IReadOnlyList<string> images)
        {
            var result = await client.ExtractAsync(options, images, position, options.ContextTokens, ct, limits.KeepAliveSeconds,
                response =>
                {
                    onCall(new(false, response.TotalDuration, response.LoadDuration, response.EvaluationDuration));
                }, () => onCall(new(true)), () => onCall(new(false, CacheHit: true)), request.ForceRefresh).ConfigureAwait(false);
            OllamaResultMapper.Validate(result);
            results.Add(result); position += images.Count;
        }
        {
            var encoded = new List<string>();
            long bytes = 0, pixels = 0;
            foreach (var image in request.Images)
            {
                ct.ThrowIfCancellationRequested();
                if (encoded.Count >= Math.Min(limits.MaxImagesInMemory, options.BatchSize))
                { await ExtractAsync(encoded).ConfigureAwait(false); encoded.Clear(); bytes = pixels = 0; }
                var input = await discovery.LoadVerifiedAsync(image, ct).ConfigureAwait(false);
                var prepared = preprocessing.Prepare(input, options, ct);
                var content = prepared.Bytes;
                long count = prepared.Pixels;
                if (encoded.Count > 0 && (encoded.Count >= Math.Min(limits.MaxImagesInMemory, options.BatchSize) ||
                    bytes + content.Length > options.MaxBatchImageBytes || pixels + count > options.MaxBatchPixels))
                { await ExtractAsync(encoded).ConfigureAwait(false); encoded.Clear(); bytes = pixels = 0; }
                encoded.Add(Convert.ToBase64String(content.Span)); bytes += content.Length; pixels += count;
            }
            if (encoded.Count > 0) await ExtractAsync(encoded).ConfigureAwait(false);
        }
        if (results.Count == 0) throw new ArgumentException("O grupo não contém imagens disponíveis para análise.");
        return OllamaResultMapper.Map(results, options.Model, request.Images.Count, ct);
    }
}
