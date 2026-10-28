using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Application.Interfaces;

public interface IJobImageAnalyzer
{
    Task<JobImageAnalysisResult> AnalyzeAsync(IReadOnlyList<JobImageInput> images,
        OllamaOptions options, IProgress<ImageAnalysisProgress>? progress, CancellationToken cancellationToken);
}

public interface IOllamaConnection
{
    Task<string> AnalysisIdentityAsync(OllamaOptions options, CancellationToken cancellationToken);
    Task VerifyAsync(OllamaOptions options, CancellationToken cancellationToken);
}
