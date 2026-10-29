using JobTracker.Modules.Settings.Application;
using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;
using Microsoft.Extensions.Logging;

namespace JobTracker.Presentation.ViewModels;

public sealed class SettingsViewModel : ObservableModel
{
    private readonly IPreferences _preferences;
    private readonly IDatabaseMaintenance _database;
    private readonly DataTransferService _transfer;
    private readonly DesktopServices _desktop;
    private readonly JobWorkspace _workspace;
    private readonly UiOperationState _operations;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _dark, _reduceMotion;

    public SettingsViewModel(IPreferences preferences, IDatabaseMaintenance database, DataTransferService transfer,
        DesktopServices desktop, JobWorkspace workspace, UiOperationState operations, OllamaSettingsViewModel ollama,
        ILogger<SettingsViewModel> logger)
    {
        _preferences = preferences; _database = database; _transfer = transfer;
        _desktop = desktop; _workspace = workspace; _operations = operations; _logger = logger; Ollama = ollama;
        SaveSettingsCommand = operations.Command(nameof(SaveSettingsCommand), SaveSettingsAsync);
        OpenFolderCommand = operations.Command(nameof(OpenFolderCommand), _ => { desktop.OpenFolder(DatabasePath); return Task.CompletedTask; });
        ExportJsonCommand = operations.Command(nameof(ExportJsonCommand), ct => ExportAsync(false, ct));
        ExportCsvCommand = operations.Command(nameof(ExportCsvCommand), ct => ExportAsync(true, ct));
        ImportJsonCommand = operations.Command(nameof(ImportJsonCommand), ct => ImportAsync(false, ct));
        ImportCsvCommand = operations.Command(nameof(ImportCsvCommand), ct => ImportAsync(true, ct));
        BackupCommand = operations.Command(nameof(BackupCommand), BackupAsync);
        RestoreCommand = operations.Command(nameof(RestoreCommand), RestoreAsync);
    }

    public event Func<CancellationToken, Task>? DatabaseRestored;
    public OllamaSettingsViewModel Ollama { get; }
    public bool Dark { get => _dark; set => Set(ref _dark, value); }
    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }
    public string DatabasePath => _database.DatabasePath;
    public AsyncUiCommand SaveSettingsCommand { get; }
    public AsyncUiCommand OpenFolderCommand { get; }
    public AsyncUiCommand ExportJsonCommand { get; }
    public AsyncUiCommand ExportCsvCommand { get; }
    public AsyncUiCommand ImportJsonCommand { get; }
    public AsyncUiCommand ImportCsvCommand { get; }
    public AsyncUiCommand BackupCommand { get; }
    public AsyncUiCommand RestoreCommand { get; }

    public async Task LoadAsync(CancellationToken ct)
    {
        Dark = await _preferences.GetAsync("DarkTheme", ct) == "true";
        ReduceMotion = await _preferences.GetAsync("ReduceMotion", ct) == "true";
        try { await Ollama.LoadAsync(ct); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning("Preferências Ollama inválidas: {Type}", ex.GetType().Name);
            _operations.Message = ex.Message + " Revise a seção Ollama em Configurações.";
        }
        _desktop.SetTheme(Dark, ReduceMotion);
    }

    private async Task SaveSettingsAsync(CancellationToken ct)
    {
        await _preferences.SetManyAsync(new Dictionary<string, string>
        {
            ["DarkTheme"] = Dark ? "true" : "false",
            ["ReduceMotion"] = ReduceMotion ? "true" : "false"
        }, ct);
        _desktop.SetTheme(Dark, ReduceMotion);
    }

    private async Task ExportAsync(bool csv, CancellationToken ct)
    {
        string? path = _desktop.SavePath(csv ? "csv" : "json");
        if (path is null) return;
        await _transfer.ExportAsync(path, csv, ct);
        _operations.Message = "Exportação concluída.";
    }

    private async Task ImportAsync(bool csv, CancellationToken ct)
    {
        string? input = _desktop.OpenPath(csv ? "csv" : "json");
        if (input is null) return;
        ImportPreview preview = await _transfer.PreviewAsync(input, csv, ct);
        string report = preview.Summary + "\n\n" + string.Join("\n", preview.Lines.Select(l => $"{l.Number}. {l.Title} — {l.Result}"));
        if (preview.Accepted.Count > 0 && _desktop.Confirm("Prévia da importação", report + "\n\nImportar os registros aceitos, incluindo as oportunidades semelhantes indicadas?"))
        {
            await _transfer.ImportAsync(preview, ct);
            await _workspace.ReloadAsync(ct);
            _operations.Message = "Importação concluída: " + preview.Summary;
        }
        else _operations.Message = "Nenhum dado importado. " + preview.Summary;
        if (preview.Accepted.Count == 0) _desktop.Confirm("Relatório da importação", report + "\n\nNenhum dado será importado.");
    }

    private async Task BackupAsync(CancellationToken ct)
    {
        string? destination = _desktop.SavePath("db");
        if (destination is null) return;
        await _database.BackupAsync(destination, ct);
        _operations.Message = "Backup consistente criado.";
    }

    private async Task RestoreAsync(CancellationToken ct)
    {
        string? source = _desktop.OpenPath("db");
        if (source is null || !_desktop.Confirm("Restaurar banco", "O banco selecionado substituirá todos os dados atuais, incluindo preferências. Um backup de segurança será criado na pasta local antes da substituição. Deseja continuar?")) return;
        string safety = await _database.RestoreAsync(source, ct);
        // Restoration is committed; refresh all presentation state even if cancellation arrived meanwhile.
        await LoadAsync(CancellationToken.None);
        if (DatabaseRestored is { } restored) await restored(CancellationToken.None);
        await _workspace.ReloadAsync(CancellationToken.None);
        _operations.Message = "Banco restaurado. Backup anterior: " + safety;
    }
}
