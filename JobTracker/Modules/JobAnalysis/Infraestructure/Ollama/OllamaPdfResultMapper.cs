using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

internal static class OllamaPdfResultMapper
{
    public static OllamaPdfJobData Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("O resultado deve ser um objeto.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException("Propriedade duplicada.");

        var data = JsonSerializer.Deserialize<OllamaPdfJobData>(json, OllamaExtractionContract.ExtractionJsonOptions)
            ?? throw new JsonException("Resultado nulo.");
        Validate(data);
        return data;
    }

    private static void Validate(OllamaPdfJobData data)
    {
        foreach (var (value, maximum) in new[]
        {
            (data.Title, 300), (data.Company, 200), (data.Description, 100000), (data.Location, 500)
        })
        {
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > maximum ||
                JobTextKey.Normalize(value) is "nao informado" or "nao informada" or "n/a" or "null"))
                throw new JsonException("Campo textual incompatível.");
        }

        if (data.WorkModel is not (null or "Remoto" or "Híbrido" or "Presencial"))
            throw new JsonException("Modalidade incompatível.");
        if (data.Technologies is null || data.Technologies.Length > 100 ||
            data.Technologies.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 80) ||
            data.Technologies.Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Technologies.Length)
            throw new JsonException("Lista de tecnologias incompatível.");

        bool hasTitle = data.Title?.Count(char.IsLetter) >= 2;
        bool hasDescription = data.Description?.Count(char.IsLetter) >= 20;
        if (!hasTitle && !hasDescription)
            throw new ImageAnalysisException(ImageAnalysisFailure.InvalidResponse,
                "A IA não identificou cargo ou descrição suficientes para reconhecer uma vaga. " +
                "Confira se o PDF contém uma única oportunidade e se o texto foi extraído corretamente.");
    }

    public static PdfJobAnalysisResult Map(OllamaPdfJobData data)
    {
        Validate(data);
        var content = new JobContent(
            data.Title?.Trim() ?? "", data.Company?.Trim() ?? "", data.Description?.Trim() ?? "",
            data.Location?.Trim() ?? "", data.WorkModel ?? "", "", "", "", "", "", "", "", null,
            string.Join(", ", data.Technologies.Select(value => value.Trim())));

        var warnings = new List<string> { "Sugestões de IA local: confira cada campo no PDF. Nenhuma vaga foi salva." };
        if (data.Title is null) warnings.Add("Cargo não identificado.");
        if (data.Company is null) warnings.Add("Empresa não identificada.");
        return new PdfJobAnalysisResult(content, warnings.AsReadOnly());
    }
}
