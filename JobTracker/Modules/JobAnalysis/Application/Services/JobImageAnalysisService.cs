using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Application.Services;

public sealed class JobImageAnalysisService(IJobImageAnalyzer analyzer)
{

    public async Task<JobImageAnalysisResult> AnalyzeAsync(IReadOnlyList<JobImageInput> images,
        OllamaOptions options, IProgress<ImageAnalysisProgress>? progress, CancellationToken ct)
    {
        if (images.Count == 0) throw new ArgumentException("Adicione pelo menos um print antes de analisar.");
        var result = await analyzer.AnalyzeAsync(images, options, progress, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        result = result with { Content = result.Content with { Technologies = TechnologyCatalog.NormalizeNames(result.Content.Technologies) } };
        Validate(result);
        return result;
    }

    private static void Validate(JobImageAnalysisResult result)
    {
        var content = result.Content;
        foreach (var (value, limit) in new[] { (content.Title, 300), (content.Company, 200), (content.Description, 200000),
                     (content.Location, 500), (content.WorkMode, 500), (content.Seniority, 500), (content.Contract, 500),
                     (content.Salary, 500), (content.Requirements, 50000), (content.Technologies, 8000), (result.Url, 8000) })
            if (value.Length > limit) throw new InvalidOperationException("O resultado excede os limites do formulário. Nenhum conteúdo foi cortado; reduza o conjunto de prints.");
        string[] tags = content.Technologies.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tags.Length > 100 || tags.Any(t => t.Length > 80))
            throw new InvalidOperationException("O resultado excede o limite de 100 tecnologias, com até 80 caracteres por tecnologia.");
        if (result.Url.Length > 0 && (!Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0))
            throw new InvalidOperationException("A análise produziu uma URL inválida. Nenhum campo foi aplicado.");
        result.Capture?.Validate();
    }
}
