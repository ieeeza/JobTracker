using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed class OllamaJobImageAnalyzer(OllamaClient client, ImagePreparationService preprocessing,
    ImageLimits imageLimits) : IJobImageAnalyzer, IDisposable
{
    private readonly SemaphoreSlim _inference = new(1, 1);

    public async Task<JobImageAnalysisResult> AnalyzeAsync(IReadOnlyList<JobImageInput> images,
        OllamaOptions options, IProgress<ImageAnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        options.Validate();
        if (images.Count == 0 || images.Count > options.MaxImages)
            throw new ArgumentException($"A IA aceita de 1 a {options.MaxImages} prints por análise. Ajuste o limite em Configurações se necessário.");
        if (images.Sum(i => i.EstimatedMemoryBytes) > imageLimits.MaxBatchBytes)
            throw new ArgumentException("O conjunto excede o limite de memória das imagens.");
        // Large KV caches can move model layers off an 8 GB GPU and onto the CPU.
        // Send each original image once with a smaller context, then use the existing validated merge.
        if (options.PreferLowMemoryIndividualAnalysis)
        {
            int context = Math.Min(options.ContextTokens, OllamaOptions.LowMemoryContextTokens);
            options = options with { ContextTokens = context, MaxContextTokens = context, BatchSize = 1 };
        }
        // Serialize across callers, including model loading. Cancellation never applies a partial result.
        await _inference.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await client.VerifyAsync(options, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() => AnalyzeBatchesAsync(images, options, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { _inference.Release(); }
    }

    private async Task<JobImageAnalysisResult> AnalyzeBatchesAsync(IReadOnlyList<JobImageInput> images, OllamaOptions options,
        IProgress<ImageAnalysisProgress>? progress, CancellationToken ct)
    {
        var results = new List<OllamaJobData>();
        var encoded = new List<string>();
        var batchImages = new List<JobImageInput>();
        long bytes = 0, pixels = 0;
        int completed = 0;

        async Task FlushAsync()
        {
            if (encoded.Count == 0) return;
            foreach (var image in batchImages)
                progress?.Report(new(image.Id, completed + 1, images.Count, ImageAnalysisStatus.Processing,
                    $"Analisando lote {results.Count + 1} no Ollama ({batchImages.Count} imagens, contexto {options.ContextTokens:N0})…"));
            var result = await ExtractBatchAsync().ConfigureAwait(false);
            OllamaResultMapper.Validate(result);
            results.Add(result);
            foreach (var image in batchImages)
            {
                completed++;
                progress?.Report(new(image.Id, completed, images.Count, ImageAnalysisStatus.Completed, "Lote recebido; aguardando validação do conjunto."));
            }
            encoded.Clear(); batchImages.Clear(); bytes = pixels = 0;
        }

        async Task<OllamaJobData> ExtractBatchAsync()
        {
            int contextTokens = options.ContextTokens;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await client.ExtractAsync(options, encoded, completed + 1, contextTokens, ct).ConfigureAwait(false);
                }
                catch (ImageAnalysisException ex) when (ex.Failure == ImageAnalysisFailure.ContextExhausted && contextTokens < options.MaxContextTokens)
                {
                    // Retry only a truncated generation, with the original images and a larger budget.
                    // Never combine incomplete JSON or retry memory/transport failures indefinitely.
                    contextTokens = Math.Min(contextTokens * 2, options.MaxContextTokens);
                    foreach (var image in batchImages)
                        progress?.Report(new(image.Id, completed + 1, images.Count, ImageAnalysisStatus.Processing,
                            $"O Ollama precisou de mais contexto. Reanalisando o lote {results.Count + 1} com {contextTokens:N0} tokens. Você pode cancelar."));
                }
                catch (ImageAnalysisException ex) when (ex.Failure == ImageAnalysisFailure.ContextExhausted && options.PreferLowMemoryIndividualAnalysis)
                {
                    throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
                        "Esta captura exige mais contexto que o modo de economia de memória permite. " +
                        "Recorte-a em partes legíveis ou desative a economia de memória em Configurações → Ollama. " +
                        "A imagem não foi reanalisada automaticamente e nenhum resultado parcial foi aplicado.", ex);
                }
            }
        }

        foreach (var image in images)
        {
            ct.ThrowIfCancellationRequested();
            // Decode with existing format/dimension guards. BMP is converted losslessly to PNG.
            // Preparation preserves detail; thumbnails are never sent to the model.
            if (encoded.Count >= options.BatchSize) await FlushAsync().ConfigureAwait(false);
            var prepared = preprocessing.Prepare(image, options, ct);
            if (prepared.Bytes.Length > options.MaxBatchImageBytes || prepared.Pixels > options.MaxBatchPixels)
                throw new ArgumentException("Um print excede os limites de bytes ou pixels do lote IA. Recorte a captura em partes legíveis ou ajuste os limites em Configurações.");
            if (encoded.Count >= options.BatchSize || bytes + prepared.Bytes.Length > options.MaxBatchImageBytes || pixels + prepared.Pixels > options.MaxBatchPixels)
                await FlushAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            encoded.Add(Convert.ToBase64String(prepared.Bytes.Span));
            batchImages.Add(image);
            bytes += prepared.Bytes.Length; pixels += prepared.Pixels;
        }
        await FlushAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return OllamaResultMapper.Map(results, options.Model, images.Count, ct);
    }

    public void Dispose() => _inference.Dispose();
}
