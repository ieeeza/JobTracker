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
    internal async Task<IReadOnlyList<OllamaJobData>> ExtractPdfDocumentAsync(string text, OllamaOptions options,
        IProgress<PdfAnalysisProgress>? progress, CancellationToken ct)
    {
        // Preflight all resource limits before HTTP or loading a model.
        var effective = OllamaPdfPlan.LimitOptions(options.ForPdfAnalysis());
        _ = OllamaPdfPlan.Create(text, effective, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(OllamaPdfPlan.MaximumSeconds));
        var timer = Stopwatch.StartNew();
        int completed = 0;
        try
        {
            string digest = await ModelIdentityAsync(effective, deadline.Token, requiresVision: false).ConfigureAwait(false);
            effective = OllamaPdfPlan.LimitOptions(effective, PdfModelContext(effective, digest));
            var plan = OllamaPdfPlan.Create(text, effective, deadline.Token);
            logger.LogInformation("Ollama PDF documento: blocos {Blocks}; caracteres {Characters}; total estimado sem divisão {Tokens}; limite efetivo {Context}",
                plan.Blocks.Count, text.Length, plan.Estimate.RequiredTokens, effective.MaxContextTokens);
            var results = new List<OllamaJobData>(plan.Blocks.Count);
            foreach (var block in plan.Blocks)
            {
                deadline.Token.ThrowIfCancellationRequested();
                progress?.Report(new(completed + 1, plan.Blocks.Count, block.ContextTokens));
                var result = await ExtractPdfCoreAsync(block.Text, options, deadline.Token, block.Prefix,
                    plan.Blocks.Count > 1, effective.MaxContextTokens, digest).ConfigureAwait(false);
                results.Add(result);
                completed++;
            }
            // Validate the consolidated result before handing anything to the presentation layer.
            _ = OllamaResultMapper.MapPdf(results, deadline.Token);
            return results.AsReadOnly();
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new ImageAnalysisException(ImageAnalysisFailure.Timeout,
                "A análise do PDF ultrapassou 15 minutos. Nenhum resultado parcial foi apresentado. Reduza o texto e tente novamente.", ex);
        }
        finally
        {
            logger.LogInformation("Ollama PDF documento: blocos concluídos {Blocks}; duração {Seconds}s", completed, timer.Elapsed.TotalSeconds);
        }
    }

    internal async Task<OllamaJobData> ExtractPdfAsync(
        string text, OllamaOptions options, CancellationToken cancellationToken)
        => await ExtractPdfCoreAsync(text, options, cancellationToken, OllamaPdfExtractionContract.UserPrefix,
            false, OllamaPdfPlan.AutomaticMaximumContext, null).ConfigureAwait(false);

    private async Task<OllamaJobData> ExtractPdfCoreAsync(string text, OllamaOptions options, CancellationToken cancellationToken,
        string prefix, bool fragment, int maximumContext, string? expectedDigest)
    {
        var total = Stopwatch.StartNew();
        string operation = Guid.NewGuid().ToString("N");
        string outcome = "failed";
        bool acquired = false;
        try
        {
            options = OllamaPdfPlan.LimitOptions(options.ForPdfAnalysis(), maximumContext);
            var budget = OllamaPdfContextBudget.Estimate(text, prefix, fragment);
            logger.LogInformation("Ollama PDF {Operation}: modelo {Model}; bytes {TextBytes}; texto {TextTokens}; instruções {SystemTokens}; prefixo {PrefixTokens}; schema nas mensagens {SchemaTokens}; enquadramento {MessageTokens}; saída reservada {OutputTokens}; margem {SafetyTokens}; total estimado {RequiredTokens}; máximo {MaximumContext}",
                operation, options.Model, budget.TextBytes, budget.TextTokens, budget.SystemPromptTokens, budget.PrefixTokens,
                budget.SchemaPromptTokens, OllamaPdfContextBudget.MessageOverheadTokens, budget.OutputTokens, budget.SafetyTokens,
                budget.RequiredTokens, options.MaxContextTokens);
            int contextTokens = budget.SelectContextTokens(options);
            var waiting = Stopwatch.StartNew();
            try
            {
                await _inference.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
            }
            finally
            {
                logger.LogInformation("Ollama PDF {Operation}: contexto planejado {ContextTokens}; espera por inferência {WaitSeconds}s",
                    operation, contextTokens, waiting.Elapsed.TotalSeconds);
            }
            var verification = Stopwatch.StartNew();
            string digest;
            try { digest = await ModelIdentityAsync(options, cancellationToken, requiresVision: false).ConfigureAwait(false); }
            finally { logger.LogInformation("Ollama PDF {Operation}: verificação do modelo {VerificationSeconds}s", operation, verification.Elapsed.TotalSeconds); }
            if (expectedDigest is not null && expectedDigest != digest)
                throw new ImageAnalysisException(ImageAnalysisFailure.ModelMissing,
                    "O modelo mudou durante a análise. Nenhum resultado foi combinado; execute novamente.");
            options = OllamaPdfPlan.LimitOptions(options, PdfModelContext(options, digest));
            contextTokens = budget.SelectContextTokens(options);
            var generationOptions = new OllamaGenerationOptions(contextTokens)
            {
                NumPredict = checked(contextTokens - (int)budget.InputTokens)
            };
            logger.LogInformation("Ollama PDF {Operation}: contexto efetivo {ContextTokens}; limite de geração {OutputLimit}",
                operation, contextTokens, generationOptions.NumPredict);
            string cacheKey = PdfCacheKey(text, digest, options, generationOptions, prefix, fragment);
            if (await cache.ReadAsync(cacheKey, cancellationToken).ConfigureAwait(false) is { } saved)
            {
                var cached = ParsePdfBlock(saved, fragment, cancellationToken);
                outcome = "cache_hit";
                return cached;
            }

            var body = new OllamaChatRequest(options.Model,
                [new("system", OllamaPdfExtractionContract.SystemPrompt),
                 new("user", prefix + text)],
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
                    Math.Min(options.TimeoutSeconds, 300), cancellationToken).ConfigureAwait(false);
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

            if (response.PromptTokenCount is { } actualInput && actualInput + (response.GeneratedTokenCount ?? 0) >= contextTokens)
                throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
                    "A requisição atingiu a janela de contexto. O resultado foi rejeitado para evitar perda silenciosa; reduza o trecho no editor.");
            var data = ParsePdfBlock(message.Content, fragment, cancellationToken);
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

    private static OllamaJobData ParsePdfBlock(string json, bool fragment, CancellationToken ct)
    {
        if (!fragment) return OllamaResultMapper.ParsePdf(json, ct);
        ct.ThrowIfCancellationRequested();
        var data = OllamaResultMapper.Parse(json);
        if (new[] { data.Company, data.Title, data.Description, data.Location, data.WorkMode, data.Seniority,
                data.EmploymentType, data.Salary, data.Url }.All(string.IsNullOrWhiteSpace) &&
            data.Responsibilities.Length + data.Requirements.Length + data.PreferredRequirements.Length +
            data.Benefits.Length + data.Technologies.Length == 0)
            throw new ImageAnalysisException(ImageAnalysisFailure.InvalidResponse,
                "Um bloco não retornou informações aproveitáveis. Revise e limpe o texto; a análise parcial não será apresentada como completa.");
        return data;
    }

    private static string PdfCacheKey(string text, string modelDigest, OllamaOptions options, OllamaGenerationOptions generationOptions,
        string prefix, bool fragment)
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
        string identity = string.Join('\n', "pdf-text-v3", textHash, modelDigest, prefix, fragment.ToString(),
            OllamaPdfExtractionContract.PromptVersion,
            OllamaPdfExtractionContract.SystemPrompt, OllamaExtractionContract.Schema.ToJsonString(), generation);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
