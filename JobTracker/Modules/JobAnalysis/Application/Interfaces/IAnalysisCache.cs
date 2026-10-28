namespace JobTracker.Modules.JobAnalysis.Application.Interfaces;

public interface IAnalysisCache
{
    Task<string?> ReadAsync(string key, CancellationToken ct);
    Task WriteAsync(string key, string payload, CancellationToken ct);
}
