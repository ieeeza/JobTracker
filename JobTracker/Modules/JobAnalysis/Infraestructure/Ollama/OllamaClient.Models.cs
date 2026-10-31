using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed partial class OllamaClient
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset CheckedAt, OllamaShowResponse Details)> _modelCapabilities = new();
    private static readonly TimeSpan ModelCapabilitiesLifetime = TimeSpan.FromMinutes(5);

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
