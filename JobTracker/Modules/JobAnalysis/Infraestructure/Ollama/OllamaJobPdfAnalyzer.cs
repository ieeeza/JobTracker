using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

public sealed class OllamaJobPdfAnalyzer(OllamaClient client, ImageAnalysisSettings settings) : IJobPdfAnalyzer
{
    public async Task<PdfJobAnalysisResult> AnalyzeAsync(
        string extractedText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OllamaOptions options = await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var data = await client.ExtractPdfAsync(extractedText, options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return OllamaPdfResultMapper.Map(data);
    }
}
