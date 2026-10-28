using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.BatchImport;
using Microsoft.Extensions.Logging;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed class OllamaClient(HttpClient http, IAnalysisCache cache, ILogger<OllamaClient> logger) : IOllamaConnection, IDisposable
{
    private readonly SemaphoreSlim _inference = new(1, 1);
    private const int MaximumResponseBytes = 2 * 1024 * 1024;


    public async Task VerifyAsync(OllamaOptions options, CancellationToken cancellationToken) =>
        _ = await ModelIdentityAsync(options, cancellationToken).ConfigureAwait(false);

    public async Task<string> AnalysisIdentityAsync(OllamaOptions options, CancellationToken cancellationToken)
    {
        string digest = await ModelIdentityAsync(options, cancellationToken).ConfigureAwait(false);
        return CacheKey(options, [], digest + OllamaExtractionContract.SystemPrompt + OllamaGroupingContract.Prompt + OllamaGroupingContract.Schema.ToJsonString(), options.ContextTokens);
    }

    private async Task<string> ModelIdentityAsync(OllamaOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.ServerUri(), "api/tags"));
        var tags = await SendAsync<OllamaTagsResponse>(request, 10, cancellationToken).ConfigureAwait(false);
        if (tags.Models is null || tags.Models.Any(m => m is null || string.IsNullOrWhiteSpace(m.Name)))
            throw InvalidResponse();
        string expected = options.Model.Contains(':') ? options.Model : options.Model + ":latest";
        var model = tags.Models.FirstOrDefault(m => m is not null && (m.Name == expected || m.Model == expected));
        if (model is null)
            throw new ImageAnalysisException(ImageAnalysisFailure.ModelMissing,
                $"O modelo {options.Model} não foi encontrado no Ollama. Verifique se está instalado.");
        if (!string.IsNullOrWhiteSpace(model.RemoteHost) || !string.IsNullOrWhiteSpace(model.RemoteModel))
            throw new ImageAnalysisException(ImageAnalysisFailure.ModelMissing,
                "O modelo selecionado usa um servidor remoto. Selecione um modelo instalado localmente para manter os prints neste computador.");
        if (string.IsNullOrWhiteSpace(model.Digest)) throw InvalidResponse();
        return model.Digest;
    }

    internal async Task<OllamaJobData> ExtractAsync(OllamaOptions options, IReadOnlyList<string> images,
        int firstImage, int contextTokens, CancellationToken cancellationToken,
        int? keepAliveSeconds = null, Action<OllamaChatResponse>? onResponse = null, Action? onStarted = null, Action? onCacheHit = null, bool bypassCache = false)
    {
        await _inference.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ExtractCoreAsync(options, images, firstImage, contextTokens, cancellationToken, keepAliveSeconds, onResponse, onStarted, onCacheHit, bypassCache).ConfigureAwait(false); }
        finally { _inference.Release(); }
    }

    private async Task<OllamaJobData> ExtractCoreAsync(OllamaOptions options, IReadOnlyList<string> images,
        int firstImage, int contextTokens, CancellationToken cancellationToken,
        int? keepAliveSeconds, Action<OllamaChatResponse>? onResponse, Action? onStarted, Action? onCacheHit, bool bypassCache)
    {
        options.Validate();
        if (contextTokens < options.ContextTokens || contextTokens > options.MaxContextTokens)
            throw new ArgumentOutOfRangeException(nameof(contextTokens));
        var body = new OllamaChatRequest(options.Model,
            [new("system", OllamaExtractionContract.SystemPrompt),
             new("user", $"Extraia a vaga destas {images.Count} capturas, na ordem recebida. Este conjunto pode ser uma parte da vaga.", images)],
            OllamaExtractionContract.Schema, new OllamaGenerationOptions(contextTokens))
        { KeepAliveSeconds = keepAliveSeconds ?? options.KeepAliveSeconds };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ServerUri(), "api/chat"))
        { Content = JsonContent.Create(body, options: OllamaExtractionContract.JsonOptions) };
        if (images.Count == 0) throw new ArgumentException("A análise exige imagens.");
        string digest = await ModelIdentityAsync(options, cancellationToken).ConfigureAwait(false);
        string cacheKey = CacheKey(options, images, OllamaExtractionContract.SystemPrompt + digest, contextTokens);
        if (!bypassCache && await cache.ReadAsync(cacheKey, cancellationToken).ConfigureAwait(false) is { } saved)
        {
            var cached = JsonSerializer.Deserialize<OllamaJobData>(saved, OllamaExtractionContract.ExtractionJsonOptions) ?? throw InvalidResponse();
            OllamaResultMapper.Validate(cached);
            onCacheHit?.Invoke();
            return cached;
        }
        onStarted?.Invoke();
        var response = await SendAsync<OllamaChatResponse>(request, options.WaitForCompletion ? null : options.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        onResponse?.Invoke(response);
        if (!response.Done || response.Error is not null || response.DoneReason is not (null or "stop") ||
            response.Message is not { Role: "assistant" } message || string.IsNullOrWhiteSpace(message.Content))
        {
            // Log protocol metadata only: never the image, extracted text or model reasoning.
            string reason = response.DoneReason switch { null => "absent", "stop" => "stop", "length" => "length", _ => "other" };
            logger.LogWarning("Ollama: resposta rejeitada. Concluída: {Done}; motivo: {Reason}; conteúdo presente: {HasContent}; erro presente: {HasError}",
                response.Done, reason, !string.IsNullOrWhiteSpace(response.Message?.Content), response.Error is not null);
            if (response.DoneReason == "length")
                throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
                    $"O Ollama encerrou a geração sem concluir o JSON, mesmo com contexto de {contextTokens:N0} tokens. " +
                    "A espera não foi encerrada pelo aplicativo. Nenhum campo foi alterado. Reduza as imagens por lote ou ajuste o contexto máximo conforme a memória disponível.");
            throw InvalidResponse();
        }
        try
        {
            var data = JsonSerializer.Deserialize<OllamaJobData>(message.Content, OllamaExtractionContract.ExtractionJsonOptions)
                ?? throw new JsonException("Extração nula.");
            OllamaResultMapper.Validate(data);
            await cache.WriteAsync(cacheKey, JsonSerializer.Serialize(data, OllamaExtractionContract.ExtractionJsonOptions), cancellationToken).ConfigureAwait(false);
            return data;
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Ollama retornou JSON interno incompatível; dados rejeitados. Tipo: {Type}", ex.GetType().Name);
            throw InvalidResponse(ex);
        }
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, int? timeoutSeconds, CancellationToken ct) where T : class
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeoutSeconds is int seconds) deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama: HTTP {Status}; operação {Method}", (int)response.StatusCode, request.Method);
                if ((int)response.StatusCode >= 500 && await IsMemoryFailureAsync(response.Content, deadline.Token).ConfigureAwait(false))
                {
                    logger.LogWarning("Ollama informou falha de alocação de memória; conteúdo técnico omitido.");
                    throw new ImageAnalysisException(ImageAnalysisFailure.ServerFailure,
                        "O Ollama não conseguiu reservar memória para o modelo. Em Configurações → Ollama, reduza o contexto " +
                        "(por exemplo, para 4096 tokens) e as imagens por lote, salve e tente novamente. Fechar outros programas que usam GPU também pode ajudar. Nenhum campo foi alterado.");
                }
                throw response.StatusCode switch
                {
                    HttpStatusCode.NotFound when request.Method == HttpMethod.Post => new ImageAnalysisException(ImageAnalysisFailure.ModelMissing,
                        "O modelo ou endpoint de análise não foi encontrado no Ollama. Verifique a configuração e os modelos instalados."),
                    _ => new ImageAnalysisException(ImageAnalysisFailure.ServerFailure,
                        $"O Ollama recusou a operação (HTTP {(int)response.StatusCode}). Verifique memória disponível, suporte a imagens e os logs locais do Ollama,.")
                };
            }
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw InvalidResponse();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > MaximumResponseBytes) throw InvalidResponse();
                buffer.Write(chunk, 0, read);
            }
            deadline.Token.ThrowIfCancellationRequested();
            return JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), OllamaExtractionContract.JsonOptions)
                ?? throw new JsonException("Envelope nulo.");
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new ImageAnalysisException(ImageAnalysisFailure.Timeout, "O tempo limite do Ollama foi excedido. O carregamento inicial pode demorar; tente novamente, ajuste o tempo em Configurações.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            logger.LogWarning("Falha de conexão Ollama: {Type}", ex.GetType().Name);
            throw new ImageAnalysisException(ImageAnalysisFailure.Disconnected,
                "Não foi possível conectar ao Ollama local. Verifique se ele está em execução.", ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Ollama retornou envelope inválido: {Type}", ex.GetType().Name);
            throw InvalidResponse(ex);
        }
    }

    internal async Task<OllamaGroupingData> GroupScreenshotsAsync(OllamaOptions options, IReadOnlyList<string> images,
        int keepAliveSeconds, Action<BatchModelCall> onCall, CancellationToken ct)
    {
        options.Validate();
        await _inference.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (images.Count is < 1 or > 8) throw new ArgumentException("A janela visual deve conter de uma a oito imagens.");
            string digest = await ModelIdentityAsync(options, ct).ConfigureAwait(false);
            string cacheKey = CacheKey(options, images, OllamaGroupingContract.Prompt + OllamaGroupingContract.Schema.ToJsonString() + digest, options.ContextTokens);
            if (await cache.ReadAsync(cacheKey, ct).ConfigureAwait(false) is { } saved)
            {
                var cached = JsonSerializer.Deserialize<OllamaGroupingData>(saved, OllamaExtractionContract.ExtractionJsonOptions) ?? throw InvalidResponse();
                BatchGroupingResolver.Validate(cached, images.Count);
                onCall(new(false, CacheHit: true));
                return cached;
            }
            var body = new OllamaChatRequest(options.Model,
                [new("system", OllamaGroupingContract.Prompt + "\nJSON Schema:\n" + OllamaGroupingContract.Schema.ToJsonString()), new("user", $"Agrupe e extraia estas {images.Count} imagens, numeradas de 0 a {images.Count - 1}, na ordem anexada.", images)],
                OllamaGroupingContract.Schema, new OllamaGenerationOptions(options.ContextTokens))
            {
                KeepAliveSeconds = keepAliveSeconds
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ServerUri(), "api/chat"))
            {
                Content = JsonContent.Create(body, options: OllamaExtractionContract.JsonOptions)
            };

            onCall(new(true));

            var response = await SendAsync<OllamaChatResponse>(request, options.WaitForCompletion ? null : options.TimeoutSeconds, ct).ConfigureAwait(false);

            onCall(new(false, response.TotalDuration, response.LoadDuration, response.EvaluationDuration));
            if (!response.Done || response.Error is not null || response.DoneReason is not (null or "stop") ||
                response.Message is not { Role: "assistant" } message || string.IsNullOrWhiteSpace(message.Content)) throw InvalidResponse();
            try
            {
                var data = JsonSerializer.Deserialize<OllamaGroupingData>(message.Content, OllamaExtractionContract.ExtractionJsonOptions) ?? throw InvalidResponse();
                BatchGroupingResolver.Validate(data, images.Count);
                await cache.WriteAsync(cacheKey, JsonSerializer.Serialize(data, OllamaExtractionContract.ExtractionJsonOptions), ct).ConfigureAwait(false);
                return data;
            }
            catch (JsonException ex) { throw InvalidResponse(ex); }
        }
        finally { _inference.Release(); }
    }

    private static string CacheKey(OllamaOptions options, IReadOnlyList<string> images, string contract, int context)
    {
        // Ordered image-set cache: never substitute a partial window for a complete job.
        string hashes = string.Join("\n", images.Select(i => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(i)))));
        string settings = JsonSerializer.Serialize(options, OllamaExtractionContract.JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("vision-v2\n" + contract + "\n" + settings + "\n" + context + "\n" + hashes)));
    }

    internal static ImageAnalysisException InvalidResponse(Exception? inner = null) => new(ImageAnalysisFailure.InvalidResponse,
        "O Ollama retornou dados vazios, incompletos ou incompatíveis. Nenhum campo foi aplicado. Tente novamente.", inner);
    public void Dispose() => _inference.Dispose();

    private static async Task<bool> IsMemoryFailureAsync(HttpContent content, CancellationToken ct)
    {
        const int maximumErrorBytes = 16384;
        if (content.Headers.ContentLength > maximumErrorBytes) return false;
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > maximumErrorBytes) return false;
            buffer.Write(chunk, 0, read);
        }
        try
        {
            string? error = JsonSerializer.Deserialize<OllamaErrorResponse>(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)),
                OllamaExtractionContract.JsonOptions)?.Error;
            return error is not null && (error.Contains("out of memory", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("out-of-memory", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("requires more system memory", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            // A non-JSON error body is still an HTTP failure; preserve the generic diagnostic.
            return false;
        }
    }
}
