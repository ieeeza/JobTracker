using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Application.Interfaces;

public interface IJobPdfAnalyzer
{
    Task<PdfAnalysisEstimate> EstimateAsync(string text, CancellationToken cancellationToken = default);
    Task<PdfJobAnalysisResult> AnalyzeAsync(string extractedText, CancellationToken cancellationToken = default,
        IProgress<PdfAnalysisProgress>? progress = null);
}
