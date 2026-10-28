using System.Collections.ObjectModel;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobAnalysis.Application.Models;
using Microsoft.Extensions.Logging;

namespace JobTracker.Presentation;

public sealed partial class ShellViewModel
{
    private JobDuplicateMatch? _selectedDuplicate;
    public ImageImportViewModel Images { get; }
    public OllamaSettingsViewModel Ollama { get; }

    private async Task InitializeOllamaAsync(CancellationToken ct)
    {
        try
        {
            await Ollama.LoadAsync(ct);
            Images.OllamaOptions = Ollama.Configuration;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning("Preferências Ollama inválidas: {Type}", ex.GetType().Name);
            Message = ex.Message + " Revise a seção Ollama em Configurações.";
        }
    }
    public ObservableCollection<JobDuplicateMatch> DuplicateMatches { get; } = [];
    public JobDuplicateMatch? SelectedDuplicate { get => _selectedDuplicate; set => Set(ref _selectedDuplicate, value); }
    private string _duplicateSummary = "Confira correspondências após preencher empresa e cargo.";
    public string DuplicateSummary { get => _duplicateSummary; private set => Set(ref _duplicateSummary, value); }

    public void AddDroppedImages(IReadOnlyList<string> paths)
    {
        if (!IsBusy && !IsFolderImport && Page is ("Nova vaga" or "Detalhes")) _runningTask = RunAsync("ImageDrop", paths);
    }

    private async Task AnalyzeImagesAsync(CancellationToken ct)
    {
        // A supplied URL is a reference only; no browser/network access occurs here.
        if (!string.IsNullOrWhiteSpace(Editor.Url)) _identifier.Identify(Editor.Url);
        Message = "Analisando imagens com IA local. Você pode cancelar.";
        JobImageAnalysisResult result;
        try
        {
            result = await Images.AnalyzeAsync(ct);
            Ollama.ReportAnalysisSuccess();
        }
        catch (ImageAnalysisException ex)
        {
            Ollama.ReportAnalysisFailure(ex);
            _logger.LogWarning("Análise Ollama falhou: {Failure}", ex.Failure);
            throw;
        }
        ct.ThrowIfCancellationRequested();
        ApplyAnalysis(result.Content, result.Url, result.Warnings);
        Evidence = result.Evidence + " " +
            (string.IsNullOrWhiteSpace(Editor.Url) ? "Importação sem URL." : "URL usada somente como referência.");
        await RefreshDuplicatesAsync(ct);
        Message = "Análise concluída. Revise campos, avisos, imagens e correspondências. Nada foi salvo.";
    }

    private async Task RefreshDuplicatesAsync(CancellationToken ct)
    {
        var matches = await _tracking.FindDuplicatesAsync(Editor.Url, Editor.Content, Images.Capture, _editing?.Id, ct);
        DuplicateMatches.Clear();
        foreach (var match in matches) DuplicateMatches.Add(match);
        SelectedDuplicate = DuplicateMatches.FirstOrDefault();
        DuplicateSummary = matches.Count == 0 ? "Nenhuma correspondência relevante nos dados informados." :
            $"{matches.Count(m => m.Kind == DuplicateKind.Confirmed)} duplicidade(s) confirmada(s) • " +
            $"{matches.Count(m => m.Kind == DuplicateKind.Possible)} possível(is). Selecione para consultar o histórico.";
    }
}
