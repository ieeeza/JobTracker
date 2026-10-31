using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infraestructure.Ollama;
using Microsoft.Extensions.Logging;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed partial class OllamaClient
{
    internal async Task<OllamaJobData> ExtractPdfAsync(
        string text, OllamaOptions options, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        string operation = Guid.NewGuid().ToString("N");
        string outcome = "failed";
        bool acquired = false;
        try
        {
            options = options.ForPdfAnalysis();
            var budget = OllamaPdfContextBudget.Estimate(text);
            logger.LogInformation("Ollama PDF {Operation}: modelo {Model}; bytes {TextBytes}; texto {TextTokens}; instruções {SystemTokens}; prefixo {PrefixTokens}; schema nas mensagens {SchemaTokens}; enquadramento {MessageTokens}; saída reservada {OutputTokens}; margem {SafetyTokens}; total estimado {RequiredTokens}; máximo {MaximumContext}",
                operation, options.Model, budget.TextBytes, budget.TextTokens, budget.SystemPromptTokens, budget.PrefixTokens,
                budget.SchemaPromptTokens, OllamaPdfContextBudget.MessageOverheadTokens, budget.OutputTokens, budget.SafetyTokens,
                budget.RequiredTokens, options.MaxContextTokens);
            int contextTokens = budget.SelectContextTokens(options);
            var generationOptions = new OllamaGenerationOptions(contextTokens)
            {
                NumPredict = checked(contextTokens - (int)budget.InputTokens)
            };
            var waiting = Stopwatch.StartNew();
            try
            {
                await _inference.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
            }
            finally
            {
                logger.LogInformation("Ollama PDF {Operation}: contexto {ContextTokens}; limite de geração {OutputLimit}; espera por inferência {WaitSeconds}s",
                    operation, contextTokens, generationOptions.NumPredict, waiting.Elapsed.TotalSeconds);
            }
            var verification = Stopwatch.StartNew();
            string digest;
            try { digest = await ModelIdentityAsync(options, cancellationToken, requiresVision: false).ConfigureAwait(false); }
            finally { logger.LogInformation("Ollama PDF {Operation}: verificação do modelo {VerificationSeconds}s", operation, verification.Elapsed.TotalSeconds); }
            string cacheKey = PdfCacheKey(text, digest, options, generationOptions);
            if (await cache.ReadAsync(cacheKey, cancellationToken).ConfigureAwait(false) is { } saved)
            {
                var cached = OllamaResultMapper.ParsePdf(saved, cancellationToken);
                outcome = "cache_hit";
                return cached;
            }

            var body = new OllamaChatRequest(options.Model,
                [new("system", OllamaPdfExtractionContract.SystemPrompt),
                 new("user", OllamaPdfExtractionContract.UserPrefix + text)],
                OllamaExtractionContract.Schema, generationOptions)
            {
                KeepAliveSeconds = options.KeepAliveSeconds
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ServerUri(), "api/chat"))
            {
                Content = JsonContent.Create(body, options: OllamaExtractionContract.JsonOptions)
            };
            await PrepareInferenceModelAsync(options, cancellationToken).ConfigureAwait(false);
            var httpTimer = Stopwatch.StartNew();
            OllamaChatResponse response;
            try
            {
                response = await SendAsync<OllamaChatResponse>(request,
                    options.WaitForCompletion ? null : options.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
            }
            finally { logger.LogInformation("Ollama PDF {Operation}: requisição HTTP {HttpSeconds}s", operation, httpTimer.Elapsed.TotalSeconds); }
            logger.LogInformation("Ollama PDF {Operation}: carga {LoadSeconds}s; prompt real {PromptTokens}; avaliação do prompt {PromptSeconds}s; tokens gerados {GeneratedTokens}; geração {GenerationSeconds}s; total servidor {ServerSeconds}s",
                operation, response.LoadDuration / 1_000_000_000d, response.PromptTokenCount,
                response.PromptEvaluationDuration / 1_000_000_000d, response.GeneratedTokenCount,
                response.EvaluationDuration / 1_000_000_000d, response.TotalDuration / 1_000_000_000d);
            if (response.PromptTokenCount > budget.InputTokens)
                logger.LogWarning("Ollama PDF {Operation}: prompt real {ActualTokens} excedeu a estimativa de entrada {EstimatedTokens}; revisar margem para este modelo.",
                    operation, response.PromptTokenCount, budget.InputTokens);

            if (response.DoneReason == "length")
                throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
                    "O Ollama atingiu o limite de geração antes de concluir a análise do PDF. " +
                    "O resultado parcial foi rejeitado. Use um documento menor ou revise o contexto nas configurações.");
            if (!response.Done || response.Error is not null || response.DoneReason is not (null or "stop") ||
                response.Message is not { Role: "assistant" } message || string.IsNullOrWhiteSpace(message.Content))
                throw InvalidResponse();

            var data = OllamaResultMapper.ParsePdf(message.Content, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await cache.WriteAsync(cacheKey,
                JsonSerializer.Serialize(data, OllamaExtractionContract.ExtractionJsonOptions), cancellationToken).ConfigureAwait(false);
            outcome = "success";
            return data;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Ollama PDF: JSON incompatível rejeitado. Tipo: {Type}", ex.GetType().Name);
            throw InvalidResponse(ex);
        }
        finally
        {
            if (acquired) _inference.Release();
            logger.LogInformation("Ollama PDF {Operation}: resultado {Outcome}; tempo total {TotalSeconds}s",
                operation, outcome, total.Elapsed.TotalSeconds);
        }
    }

    private static string PdfCacheKey(string text, string modelDigest, OllamaOptions options, OllamaGenerationOptions generationOptions)
    {
        string textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        string generation = JsonSerializer.Serialize(new
        {
            options.Model,
            Server = options.ServerUri().AbsoluteUri,
            Options = generationOptions,
            Think = false,
            options.KeepAliveSeconds
        }, OllamaExtractionContract.JsonOptions);
        string identity = string.Join('\n', "pdf-text-v2", textHash, modelDigest,
            OllamaPdfExtractionContract.PromptVersion,
            OllamaPdfExtractionContract.SystemPrompt, OllamaExtractionContract.Schema.ToJsonString(), generation);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
