using System.Text.Json;
using System.Text.Json.Serialization;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.Settings.Application;

namespace JobTracker.Modules.JobAnalysis.Application.Services;

public sealed class ImageAnalysisSettings(IPreferences preferences)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectNullableAnnotations = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<OllamaOptions> LoadAsync(CancellationToken ct)
    {
        string? json = await preferences.GetAsync("OllamaOptions", ct);
        try
        {
            var options = json is null ? new OllamaOptions() : JsonSerializer.Deserialize<OllamaOptions>(json, JsonOptions)
                ?? throw new JsonException("Configuração nula.");
            options.Validate();
            return options;
        }
        catch (JsonException ex) { throw new InvalidOperationException("As preferências do Ollama estão inválidas. Revise e salve a configuração local.", ex); }
    }

    public async Task<JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchImportOptions> LoadBatchOptionsAsync(CancellationToken ct)
    {
        string? json = await preferences.GetAsync("VisionBatchOptions", ct);
        var options = json is null ? new JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchImportOptions() :
            JsonSerializer.Deserialize<JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchImportOptions>(json, JsonOptions)
                ?? throw new InvalidOperationException("Limites de importação inválidos.");
        options.Validate(); return options;
    }

    public Task SaveBatchOptionsAsync(JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchImportOptions options, CancellationToken ct)
    {
        options.Validate();
        return preferences.SetAsync("VisionBatchOptions", JsonSerializer.Serialize(options, JsonOptions), ct);
    }

    public Task SaveOptionsAsync(OllamaOptions options, CancellationToken ct)
    {
        options.Validate();
        return preferences.SetAsync("OllamaOptions", JsonSerializer.Serialize(options, JsonOptions), ct);
    }
}
