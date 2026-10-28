using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobAnalysis.Application.BatchImport;

public enum ImportBatchStatus { Pending, Processing, Completed, Cancelled, Failed }
public enum ImportItemStatus { Pending, Processing, Imported, Duplicate, NeedsReview, Failed, Discarded }
public enum GroupingConfidence { High, Ambiguous, ManuallyReviewed }

public static class BatchLabels
{
    public static string Label(this ImportBatchStatus status) => status switch
    {
        ImportBatchStatus.Pending => "Aguardando início",
        ImportBatchStatus.Processing => "Em processamento",
        ImportBatchStatus.Completed => "Importação concluída",
        ImportBatchStatus.Cancelled => "Importação cancelada",
        ImportBatchStatus.Failed => "Importação interrompida por falha",
        _ => throw new ArgumentException("Estado de lote inválido.")
    };
    public static string Label(this GroupingConfidence confidence) => confidence switch
    {
        GroupingConfidence.High => "alta",
        GroupingConfidence.Ambiguous => "ambígua",
        GroupingConfidence.ManuallyReviewed => "revisada manualmente",
        _ => throw new ArgumentException("Confiança de agrupamento inválida.")
    };
}

public sealed record BatchImportOptions
{
    public int MaxFiles { get; init; } = 1000;
    public bool IncludeSubdirectories { get; init; }
    public int MaxImagesInMemory { get; init; } = 3;
    public int KeepAliveSeconds { get; init; } = 600;
    [JsonExtensionData] public Dictionary<string, JsonElement>? LegacyData { get; set; }
    public void Validate()
    {
        if (MaxFiles is < 1 or > 10000 || MaxImagesInMemory is < 2 or > 8 || KeepAliveSeconds is < 0 or > 3600)
            throw new ArgumentException("Limites inválidos: arquivos 1–10.000, janela visual 2–8 e keep_alive 0–3.600 s.");
    }
}

public sealed class BatchScreenshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
    public long FileBytes { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string TimeSource { get; set; } = "";
    public int Order { get; set; }
    public Guid? GroupId { get; set; }
    public bool Analyzed { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? LegacyData { get; set; }
    public string? Error { get; set; }
    public string Display => $"{Order + 1}. {Name} • {TimeSource}" + (Error is null ? "" : $" • {Error}");
}

public sealed class DetectedJobGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public GroupingConfidence Confidence { get; set; }
    public bool BoundaryAmbiguous { get; set; }
    public ImportItemStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public JobContent? Content { get; set; }
    public string Url { get; set; } = "";
    public OcrCapture? Capture { get; set; }
    public Guid? JobId { get; set; }
    public Guid? ExistingJobId { get; set; }
    public bool Reused { get; set; }
    public bool UsedAi { get; set; }
    public bool ExtractionCompleted { get; set; }
    public bool ForceRefresh { get; set; }
    public List<Guid> InferenceImageIds { get; set; } = [];
    public string Evidence { get; set; } = "";
    public string IdentityCompany { get; set; } = "";
    public string IdentityTitle { get; set; } = "";
    public string Label => Status switch
    {
        ImportItemStatus.Imported => Reused ? "Salva anteriormente" : "Salva",
        ImportItemStatus.Duplicate => "Duplicada",
        ImportItemStatus.NeedsReview => "Revisar",
        ImportItemStatus.Failed => "Erro",
        ImportItemStatus.Discarded => "Descartada",
        ImportItemStatus.Processing => "Processando",
        _ => "Pendente"
    };
    public string Display => $"{Order + 1}. [{Label}] {Content?.Company ?? IdentityCompany} • {Content?.Title ?? IdentityTitle}";
}

public sealed class BatchMetrics
{
    public double TotalMilliseconds { get; set; }
    public double DiscoveryMilliseconds { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? LegacyData { get; set; }
    public double GroupingMilliseconds { get; set; }
    public double InferenceMilliseconds { get; set; }
    public int ModelCalls { get; set; }
    public int CacheHits { get; set; }
    public long OllamaTotalNanoseconds { get; set; }
    public long OllamaLoadNanoseconds { get; set; }
    public long OllamaEvaluationNanoseconds { get; set; }
}

public sealed class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Folder { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public ImportBatchStatus Status { get; set; }
    public int AnalysisVersion { get; set; }
    public string AnalysisIdentity { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? LegacyData { get; set; }
    public BatchImportOptions Options { get; set; } = new();
    public List<BatchScreenshot> Images { get; set; } = [];
    public List<DetectedJobGroup> Groups { get; set; } = [];
    public List<string> Notices { get; set; } = [];
    public BatchMetrics Metrics { get; set; } = new();
    public IEnumerable<BatchScreenshot> ImagesFor(DetectedJobGroup group) => Images.Where(i => i.GroupId == group.Id).OrderBy(i => i.Order);
    public string Summary => $"{Status.Label()} • {Images.Count} imagens encontradas • {Images.Count(i => i.Analyzed)} analisadas • {Groups.Count} grupos\n" +
        $"{Groups.Count(g => g.Status == ImportItemStatus.Imported && !g.Reused)} salvas • {Groups.Count(g => g.Reused)} já salvas • " +
        $"{Groups.Count(g => g.Status == ImportItemStatus.Duplicate)} duplicadas • {Groups.Count(g => g.Status == ImportItemStatus.NeedsReview)} para revisar • " +
        $"{Groups.Count(g => g.Status == ImportItemStatus.Failed)} grupos com erro • {Images.Count(i => i.Error is not null)} imagens com erro\n" +
        $"Tempo: {TimeSpan.FromMilliseconds(Metrics.TotalMilliseconds):g} • descoberta {Metrics.DiscoveryMilliseconds / 1000:F1}s • " +
        $"agrupamento {Metrics.GroupingMilliseconds / 1000:F1}s • extração adicional {Metrics.InferenceMilliseconds / 1000:F1}s • chamadas {Metrics.ModelCalls} • cache {Metrics.CacheHits}\n" +
        $"Ollama: total {Metrics.OllamaTotalNanoseconds / 1e9:F1}s • carga {Metrics.OllamaLoadNanoseconds / 1e9:F1}s • avaliação {Metrics.OllamaEvaluationNanoseconds / 1e9:F1}s";
    public static string Hash(IEnumerable<string> hashes) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashes.Order(StringComparer.Ordinal)))));
}

public sealed record BatchHistoryEntry(Guid Id, DateTime StartedAtUtc, string Folder, ImportBatchStatus Status)
{
    public string Display => $"{StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} • {Status.Label()} • {Folder}";
}
public sealed record BatchProgress(string Stage, int Completed, int Total, int Groups, int Saved, int Pending)
{
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> GroupLines { get; init; } = [];
    public bool Indeterminate => Total == 0;
    public string Display => $"{Stage} • {Completed}/{Total} • grupos {Groups} • salvas {Saved} • pendências {Pending}";
}
public interface IScreenshotDiscovery
{
    Task<ImportBatch> DiscoverAsync(string folder, BatchImportOptions options, CancellationToken ct);
    Task VerifyAsync(BatchScreenshot image, CancellationToken ct);
    Task<JobImageInput> LoadVerifiedAsync(BatchScreenshot image, CancellationToken ct);
}
public interface IImportBatchStore
{
    Task SaveAsync(ImportBatch batch, CancellationToken ct);
    Task<ImportBatch> LoadAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<BatchHistoryEntry>> HistoryAsync(CancellationToken ct);
    Task<Guid?> FindAsync(string fingerprint, CancellationToken ct);
}
public sealed record BatchInferenceRequest(IReadOnlyList<BatchScreenshot> Images, bool ForceRefresh = false);
public sealed record BatchModelCall(bool Started, long TotalNanoseconds = 0, long LoadNanoseconds = 0, long EvaluationNanoseconds = 0, bool CacheHit = false);
public interface IBatchJobInterpreter
{
    Task<JobImageAnalysisResult> InterpretAsync(BatchInferenceRequest request, OllamaOptions options, BatchImportOptions limits, Action<BatchModelCall> onCall, CancellationToken ct);
}
public sealed record ResolvedScreenshotGroup(IReadOnlyList<int> ImageIndices, string Company, string Title, bool Confident, JobImageAnalysisResult? Analysis);
public interface IBatchGroupingResolver
{
    Task<IReadOnlyList<ResolvedScreenshotGroup>> ResolveAsync(IReadOnlyList<BatchScreenshot> images, OllamaOptions options,
        BatchImportOptions limits, Action<BatchModelCall> onCall, CancellationToken ct);
}
