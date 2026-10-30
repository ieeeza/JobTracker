using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Application.Models;
using Microsoft.Extensions.Logging;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed partial class OllamaClient
{
    internal async Task<OllamaPdfJobData> ExtractPdfAsync(
        string text, OllamaOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        OllamaPdfExtractionContract.ValidateInput(text, options);

        await _inference.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string digest = await ModelIdentityAsync(options, cancellationToken).ConfigureAwait(false);
            string cacheKey = PdfCacheKey(text, digest, options);
            if (await cache.ReadAsync(cacheKey, cancellationToken).ConfigureAwait(false) is { } saved)
                return OllamaPdfResultMapper.Parse(saved);

            var body = new OllamaChatRequest(options.Model,
                [new("system", OllamaPdfExtractionContract.SystemPrompt),
                 new("user", OllamaPdfExtractionContract.UserPrefix + text)],
                OllamaPdfExtractionContract.Schema, new OllamaGenerationOptions(options.ContextTokens))
            {
                KeepAliveSeconds = options.KeepAliveSeconds
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ServerUri(), "api/chat"))
            {
                Content = JsonContent.Create(body, options: OllamaExtractionContract.JsonOptions)
            };
            var response = await SendAsync<OllamaChatResponse>(request,
                options.WaitForCompletion ? null : options.TimeoutSeconds, cancellationToken).ConfigureAwait(false);

            if (response.DoneReason == "length")
                throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
                    "O Ollama atingiu o limite de geração antes de concluir a análise do PDF. " +
                    "O resultado parcial foi rejeitado. Use um documento menor ou revise o contexto nas configurações.");
            if (!response.Done || response.Error is not null || response.DoneReason is not (null or "stop") ||
                response.Message is not { Role: "assistant" } message || string.IsNullOrWhiteSpace(message.Content))
                throw InvalidResponse();

            var data = OllamaPdfResultMapper.Parse(message.Content);
            cancellationToken.ThrowIfCancellationRequested();
            await cache.WriteAsync(cacheKey,
                JsonSerializer.Serialize(data, OllamaExtractionContract.ExtractionJsonOptions), cancellationToken).ConfigureAwait(false);
            return data;
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Ollama PDF: JSON incompatível rejeitado. Tipo: {Type}", ex.GetType().Name);
            throw InvalidResponse(ex);
        }
        finally
        {
            _inference.Release();
        }
    }

    private static string PdfCacheKey(string text, string modelDigest, OllamaOptions options)
    {
        string textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        string generation = JsonSerializer.Serialize(new
        {
            options.Model,
            Server = options.ServerUri().AbsoluteUri,
            Options = new OllamaGenerationOptions(options.ContextTokens),
            Think = false,
            options.KeepAliveSeconds
        }, OllamaExtractionContract.JsonOptions);
        string identity = string.Join('\n', "pdf-text-v1", textHash, modelDigest,
            OllamaPdfExtractionContract.PromptVersion, OllamaPdfExtractionContract.SchemaVersion,
            OllamaPdfExtractionContract.SystemPrompt, generation);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
