using System.Net;
using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobAnalysis.Application.Models;

public enum ImageAnalysisFailure { Disconnected, ModelMissing, Timeout, InvalidResponse, ServerFailure, ConflictingJobs, ContextExhausted }

public sealed class ImageAnalysisException(ImageAnalysisFailure failure, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public ImageAnalysisFailure Failure { get; } = failure;
}

public sealed record JobImageAnalysisResult(JobContent Content, OcrCapture? Capture,
    IReadOnlyList<string> Warnings, string Url,
    string Evidence, string ReviewText);

public sealed record OllamaOptions
{
    public const int LowMemoryContextTokens = 4096;
    // Missing properties in existing preferences receive this default without rewriting the database.
    public bool PreferLowMemoryIndividualAnalysis { get; init; } = true;
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";
    public string Model { get; init; } = "qwen3-vl:8b";
    public int TimeoutSeconds { get; init; } = 300;
    public bool WaitForCompletion { get; init; } = true;
    public int ContextTokens { get; init; } = 8192;
    public int MaxContextTokens { get; init; } = 32768;
    public int KeepAliveSeconds { get; init; } = 600;
    public int MaxPreparedDimension { get; init; } = 4096;
    public int BatchSize { get; init; } = 3;
    public int MaxImages { get; init; } = 30;
    public long MaxBatchImageBytes { get; init; } = 24 * 1024 * 1024;
    public long MaxBatchPixels { get; init; } = 24000000;

    public Uri ServerUri()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address)))
            throw new ArgumentException("Use um endereço local do Ollama (localhost, 127.0.0.1 ou ::1), sem caminho, credenciais ou parâmetros.");
        // Resolve localhost explicitly; screenshots must never depend on DNS or a proxy.
        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? new UriBuilder(uri) { Host = "127.0.0.1" }.Uri : uri;
    }

    public void Validate()
    {
        _ = ServerUri();
        if (Model != "qwen3-vl:8b" || string.IsNullOrWhiteSpace(Model) || Model.Length > 200 || Model.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_' or ':' or '/')) ||
            Model.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Esta versão utiliza exclusivamente o modelo local qwen3-vl:8b.");
        if (KeepAliveSeconds is < 0 or > 3600 || MaxPreparedDimension is < 1024 or > 16000)
            throw new ArgumentException("Permanência: 0–3.600 segundos; dimensão de preparação: 1.024–16.000 pixels.");
        if (TimeoutSeconds is < 10 or > 3600 || BatchSize is < 1 or > 10 || MaxImages is < 1 or > 100 || BatchSize > MaxImages ||
            MaxBatchImageBytes is < 1024 * 1024 or > 64 * 1024 * 1024 || MaxBatchPixels is < 10000 or > 64000000)
            throw new ArgumentException("Configuração Ollama inválida: timeout 10–3600 s, lote 1–10, total 1–100, memória 1–64 MB e pixels 10.000–64.000.000.");
        if (ContextTokens is < 2048 or > 32768)
            throw new ArgumentException("O contexto do Ollama deve estar entre 2.048 e 32.768 tokens. Contextos maiores exigem mais memória.");
        if (MaxContextTokens < ContextTokens || MaxContextTokens > 262144)
            throw new ArgumentException("O contexto máximo deve ser pelo menos o contexto inicial e no máximo 262.144 tokens. A memória disponível pode impedir atingir esse valor.");
    }
}
