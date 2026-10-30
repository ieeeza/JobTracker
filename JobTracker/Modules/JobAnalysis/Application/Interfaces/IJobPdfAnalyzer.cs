using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Application.Interfaces;

public interface IJobPdfAnalyzer
{
    Task<PdfJobAnalysisResult> AnalyzeAsync(string extractedText, CancellationToken cancellationToken = default);
}
