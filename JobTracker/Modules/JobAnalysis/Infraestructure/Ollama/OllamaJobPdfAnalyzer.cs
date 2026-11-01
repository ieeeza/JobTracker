using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed class OllamaJobPdfAnalyzer(OllamaClient client, ImageAnalysisSettings settings) : IJobPdfAnalyzer
{
    public async Task<PdfAnalysisEstimate> EstimateAsync(string text, CancellationToken cancellationToken = default)
    {
        var options = await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => OllamaPdfPlan.Create(text, options.ForPdfAnalysis(), cancellationToken).Estimate,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfJobAnalysisResult> AnalyzeAsync(
        string extractedText, CancellationToken cancellationToken = default, IProgress<PdfAnalysisProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OllamaOptions options = await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var data = await client.ExtractPdfDocumentAsync(extractedText, options, progress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return OllamaResultMapper.MapPdf(data, cancellationToken);
    }
}
