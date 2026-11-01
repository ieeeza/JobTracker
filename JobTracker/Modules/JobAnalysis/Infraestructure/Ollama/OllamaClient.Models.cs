using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using JobTracker.Modules.JobAnalysis.Application.Models;
using Microsoft.Extensions.Logging;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed partial class OllamaClient
{
    private int PdfModelContext(OllamaOptions options, string digest)
    {
        string key = string.Join('\n', options.ServerUri().AbsoluteUri, options.Model, digest);
        if (_modelCapabilities.TryGetValue(key, out var entry) && entry.Details.ModelInfo is { } info &&
            info.TryGetValue("general.architecture", out var architecture) && architecture.ValueKind == System.Text.Json.JsonValueKind.String &&
            info.TryGetValue(architecture.GetString() + ".context_length", out var context) &&
            context.ValueKind == System.Text.Json.JsonValueKind.Number &&
            context.TryGetInt32(out int length) && length > 0) return length;
        throw new ImageAnalysisException(ImageAnalysisFailure.ModelMissing,
            "O Ollama não informou o limite de contexto textual do modelo. Verifique o modelo local antes de analisar o PDF.");
    }

    private readonly ConcurrentDictionary<string, (DateTimeOffset CheckedAt, OllamaShowResponse Details)> _modelCapabilities = new();
    private static readonly TimeSpan ModelCapabilitiesLifetime = TimeSpan.FromMinutes(5);
    // Accessed only while holding _inference. Track our requests; never scan/unload unrelated models.
    private (Uri Server, string Model)? _lastInferenceModel;

    private async Task PrepareInferenceModelAsync(OllamaOptions options, CancellationToken ct)
    {
        var current = (Server: options.ServerUri(), options.Model);
        if (_lastInferenceModel is { } previous && previous != current)
        {
            var timer = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(previous.Server, "api/generate"))
            {
                Content = JsonContent.Create(new OllamaUnloadRequest(previous.Model), options: OllamaExtractionContract.JsonOptions)
            };
            var response = await SendAsync<OllamaUnloadResponse>(request, 30, ct).ConfigureAwait(false);
            if (!response.Done || response.Error is not null) throw InvalidResponse();
            _lastInferenceModel = null;
            logger.LogInformation("Ollama: troca de modelo; anterior {PreviousModel}; próximo {NextModel}; descarregamento {UnloadSeconds}s",
                previous.Model, current.Model, timer.Elapsed.TotalSeconds);
        }
        // Retain ownership even if generation later fails or the client cancels the request.
        _lastInferenceModel = current;
    }

    private async Task ValidateModelCapabilitiesAsync(OllamaOptions options, string digest, bool requiresVision, CancellationToken ct)
    {
        string key = string.Join('\n', options.ServerUri().AbsoluteUri, options.Model, digest);
        if (!_modelCapabilities.TryGetValue(key, out var cached) || DateTimeOffset.UtcNow - cached.CheckedAt >= ModelCapabilitiesLifetime)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ServerUri(), "api/show"))
            {
                Content = JsonContent.Create(new OllamaShowRequest(options.Model), options: OllamaExtractionContract.JsonOptions)
            };
            var details = await SendAsync<OllamaShowResponse>(request, 10, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(details.RemoteHost) || !string.IsNullOrWhiteSpace(details.RemoteModel))
                throw new ImageAnalysisException(ImageAnalysisFailure.ModelMissing,
                    "O modelo selecionado usa processamento remoto. Escolha um modelo instalado localmente.");
            if (details.Capabilities is null || details.Capabilities.Any(string.IsNullOrWhiteSpace)) throw InvalidResponse();
            cached = (DateTimeOffset.UtcNow, details);
            // Settings may change repeatedly; metadata must not grow without a bound.
            if (_modelCapabilities.Count >= 32) _modelCapabilities.Clear();
            _modelCapabilities[key] = cached;
        }
        var capabilities = cached.Details.Capabilities ?? throw InvalidResponse();
        if (!capabilities.Contains("completion", StringComparer.Ordinal) ||
            requiresVision && !capabilities.Contains("vision", StringComparer.Ordinal))
            throw new ImageAnalysisException(ImageAnalysisFailure.ModelMissing, requiresVision
                ? "O modelo selecionado não oferece análise de imagens. Escolha um modelo local com capacidade de visão."
                : "O modelo selecionado não oferece geração de texto. Escolha um modelo local compatível com PDFs.");
    }
}
