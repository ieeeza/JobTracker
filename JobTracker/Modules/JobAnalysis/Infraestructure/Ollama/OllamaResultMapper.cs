using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

internal static class OllamaResultMapper
{
    public static void Validate(OllamaJobData data)
    {
        if (data.MultipleJobs)
            throw new ImageAnalysisException(ImageAnalysisFailure.ConflictingJobs,
                "As capturas parecem pertencer a vagas diferentes. Nenhum dado foi combinado. Separe as imagens por vaga e analise novamente.");
        foreach (var (value, maximum) in new[] { (data.Company, 200), (data.Title, 300), (data.Seniority, 500),
                     (data.WorkMode, 500), (data.Location, 500), (data.EmploymentType, 500), (data.Salary, 500),
                     (data.Description, 100000), (data.Url, 8000) })
            if (value?.Length > maximum) throw OllamaClient.InvalidResponse();
        ValidateList(data.Responsibilities, 200, 2000);
        ValidateList(data.Requirements, 200, 2000);
        ValidateList(data.PreferredRequirements, 200, 2000);
        ValidateList(data.Benefits, 200, 2000);
        ValidateList(data.Technologies, 100, 80);
        string url = Clean(data.Url);
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)) throw OllamaClient.InvalidResponse();
    }

    private static void ValidateList(string[]? values, int count, int length)
    {
        if (values is null || values.Length > count || values.Any(v => v is null || v.Length > length))
            throw OllamaClient.InvalidResponse();
    }

    public static JobImageAnalysisResult Map(IReadOnlyList<OllamaJobData> batches, string model, int imageCount, CancellationToken ct)
    {
        foreach (var batch in batches) Validate(batch);
        string Scalar(string name, Func<OllamaJobData, string?> select)
        {
            string[] values = batches.Select(b => Clean(select(b))).Where(v => v.Length > 0)
                .DistinctBy(JobTextKey.Normalize).ToArray();
            if (values.Length > 1)
                throw new ImageAnalysisException(ImageAnalysisFailure.ConflictingJobs,
                    $"Os lotes contêm informações divergentes em {name}. Nenhum dado foi combinado. Revise as capturas e analise apenas uma vaga por vez.");
            return values.FirstOrDefault() ?? "";
        }
        string Join(IEnumerable<string> values) => string.Join('\n', values.Select(v => JobTextNormalizer.Normalize(Clean(v), ct))
            .Where(v => v.Length > 0).DistinctBy(JobTextKey.Normalize));
        string title = Scalar("cargo", b => b.Title), company = Scalar("empresa", b => b.Company);
        string location = Scalar("localização", b => b.Location), mode = Scalar("modalidade", b => b.WorkMode);
        string seniority = Scalar("senioridade", b => b.Seniority), contract = Scalar("contrato", b => b.EmploymentType);
        string salary = Scalar("remuneração", b => b.Salary), url = Scalar("URL", b => b.Url);
        string description = Join(batches.Select(b => b.Description ?? ""));
        string responsibilities = Join(batches.SelectMany(b => b.Responsibilities));
        string requirements = Join(batches.SelectMany(b => b.Requirements));
        string preferredRequirements = Join(batches.SelectMany(b => b.PreferredRequirements));
        if (preferredRequirements.Length > 0)
            requirements = (requirements.Length > 0 ? requirements + "\n\n" : "") + "Requisitos desejáveis\n" + preferredRequirements;
        string benefits = Join(batches.SelectMany(b => b.Benefits));
        var sections = new List<string>();
        if (description.Length > 0) sections.Add(description);
        if (responsibilities.Length > 0) sections.Add("Responsabilidades\n" + responsibilities);
        if (requirements.Length > 0) sections.Add("Requisitos\n" + requirements);
        if (benefits.Length > 0) sections.Add("Benefícios\n" + benefits);
        description = string.Join("\n\n", sections);
        string technologies = TechnologyCatalog.NormalizeNames(string.Join(", ", batches.SelectMany(b => b.Technologies).Select(Clean)));
        string[] tags = technologies.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        if (description.Length > 200000 || requirements.Length > 50000 || technologies.Length > 8000 ||
            tags.Length > 100 || tags.Any(t => t.Length > 80))
            throw new ImageAnalysisException(ImageAnalysisFailure.InvalidResponse,
                "O resultado combinado excede os limites do formulário. Nenhum conteúdo foi cortado. Reduza as capturas e tente novamente.");
        var content = new JobContent(title, company, description, location, mode, seniority, contract, salary,
            requirements, "", "", "", null, technologies);
        if (new[] { title, company, description, location, mode, seniority, contract, salary, technologies, url }.All(string.IsNullOrWhiteSpace))
            throw OllamaClient.InvalidResponse();
        var warnings = new List<string> { "Sugestões de IA local: confira cada campo nas imagens. Um JSON válido não garante informações corretas." };
        if (company.Length == 0) warnings.Add("Empresa não identificada; preencha manualmente.");
        if (title.Length == 0) warnings.Add("Cargo não identificado; preencha manualmente.");
        if (batches.Count > 1) warnings.Add($"{batches.Count} lotes combinados na ordem original. Confira se todos os prints pertencem à mesma vaga.");
        ct.ThrowIfCancellationRequested();
        // This is structured extraction, not OCR. Do not fabricate an OCR transcript or quality score.
        return new(content, null, warnings, url,
            $"IA local • Ollama/{model} • {DateTime.UtcNow:O} • {imageCount} print(s), {batches.Count} lote(s). Sugestões sujeitas a revisão.",
            JsonSerializer.Serialize(batches, new JsonSerializerOptions(OllamaExtractionContract.ExtractionJsonOptions) { WriteIndented = true }));
    }

    private static string Clean(string? value)
    {
        string trimmed = value?.Trim() ?? "";
        return JobTextKey.Normalize(trimmed) is "nao informado" or "nao informada" or "n/a" or "null" ? "" : trimmed;
    }
}
