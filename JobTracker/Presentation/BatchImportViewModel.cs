using System.Collections.ObjectModel;
using System.Globalization;
using JobTracker.Modules.JobAnalysis.Application.BatchImport;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobApplications.Application;

namespace JobTracker.Presentation;

public sealed class BatchImportViewModel : ObservableModel
{
    private readonly IScreenshotDiscovery _discovery;
    private readonly IImportBatchStore _store;
    private readonly BatchImportCoordinator _coordinator;
    private readonly BatchReviewService _review;
    private readonly ImageAnalysisSettings _settings;
    private readonly DesktopServices _dialogs;
    private readonly BatchReportExport _export;
    private readonly DesktopImageServices _desktop;
    private ImportBatch? _batch;
    private string _folder = "", _status = "Selecione uma pasta e confira a ordem antes de iniciar.", _filter = "Todas";
    private BatchScreenshot? _selectedImage, _selectedSource;
    private DetectedJobGroup? _selectedGroup, _target;
    private BatchHistoryEntry? _selectedHistory;
    private bool _busy, _indeterminate, _allowSimilar;
    private int _completed, _total = 1;
    private EditorViewModel _editor = new();
    private bool _running;
    private string _progressSummary = "";
    public BatchImportViewModel(IScreenshotDiscovery discovery, IImportBatchStore store, BatchImportCoordinator coordinator,
        BatchReviewService review, ImageAnalysisSettings settings, DesktopImageServices desktop, DesktopServices dialogs, BatchReportExport export)
    {
        _discovery = discovery; _store = store; _coordinator = coordinator; _review = review; _settings = settings; _desktop = desktop; _dialogs = dialogs; _export = export;
        Action = new UiCommand(action => ActionRequested?.Invoke(action), () => !IsBusy);
    }
    public event Action<string?>? ActionRequested;
    public event EventHandler? ReportReady;
    public UiCommand Action { get; }
    public bool IsBusy { get => _busy; set { Set(ref _busy, value); Raise(nameof(IsIdle)); Raise(nameof(CanReorder)); Action.Refresh(); } }
    public bool IsIdle => !IsBusy;
    public string Folder { get => _folder; set => Set(ref _folder, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Indeterminate { get => _indeterminate; private set => Set(ref _indeterminate, value); }
    public int Completed { get => _completed; private set => Set(ref _completed, value); }
    public int Total { get => _total; private set => Set(ref _total, Math.Max(1, value)); }
    public string Summary => _running ? _progressSummary : _batch?.Summary ?? "Nenhum lote selecionado.";
    public bool IsRunning { get => _running; private set => Set(ref _running, value); }
    public string Notices => _batch is null ? "" : string.Join('\n', _batch.Notices);
    public string ImageCount => $"{Images.Count} imagens encontradas. Selecione uma imagem para ajustar sua ordem.";
    public bool CanReorder => _batch is { Groups.Count: 0 } && IsIdle;
    public string MaxFiles { get; set; } = "1000";
    public string MaxImagesInMemory { get; set; } = "3";
    public string KeepAliveSeconds { get; set; } = "600";
    public bool AllowSimilar { get => _allowSimilar; set => Set(ref _allowSimilar, value); }
    public IReadOnlyList<string> Filters { get; } = ["Todas", "Salvas", "Duplicadas", "Pendentes", "Com erro"];
    public string Filter { get => _filter; set { Set(ref _filter, value); FilterGroups(); } }
    public ObservableCollection<BatchScreenshot> Images { get; } = [];
    public ObservableCollection<BatchScreenshot> SourceImages { get; } = [];
    public ObservableCollection<DetectedJobGroup> Groups { get; } = [];
    public ObservableCollection<DetectedJobGroup> AllGroups { get; } = [];
    public ObservableCollection<BatchHistoryEntry> History { get; } = [];
    public ObservableCollection<string> ProcessingGroups { get; } = [];
    public BatchScreenshot? SelectedImage { get => _selectedImage; set => Set(ref _selectedImage, value); }
    public BatchScreenshot? SelectedSource { get => _selectedSource; set => Set(ref _selectedSource, value); }
    public DetectedJobGroup? Target { get => _target; set => Set(ref _target, value); }
    public BatchHistoryEntry? SelectedHistory { get => _selectedHistory; set => Set(ref _selectedHistory, value); }
    public DetectedJobGroup? SelectedGroup
    {
        get => _selectedGroup;
        set { if (Set(ref _selectedGroup, value)) LoadGroup(); }
    }
    public EditorViewModel ReviewEditor { get => _editor; private set => Set(ref _editor, value); }
    public string GroupDetails => SelectedGroup is not { } group ? "Selecione um grupo no relatório." :
        $"{group.Label} • confiança: {group.Confidence.Label()}\n{group.Reason}\nRegistro salvo: {group.JobId} • registro correspondente: {group.ExistingJobId}\n" +
        $"Imagens: {string.Join(", ", SourceImages.Select(i => i.Name))}\n{group.Evidence}";
    public string OriginalText => SelectedGroup?.Capture?.Text ?? SelectedGroup?.Evidence ?? "";

    public async Task ExecuteAsync(string action, CancellationToken ct)
    {
        switch (action)
        {
            case "BatchFolder":
                string? folder = _desktop.SelectFolder();
                if (folder is null) return;
                Folder = folder;
                await DiscoverAsync(ct); break;
            case "BatchDiscover": await DiscoverAsync(ct); break;
            case "BatchUp": Move(-1); break;
            case "BatchDown": Move(1); break;
            case "BatchStart": await RunAsync(ct); break;
            case "BatchExport":
                var report = RequireBatch();
                string? path = _dialogs.SavePath("json");
                if (path is not null) { await _export.ExportAsync(report, path, ct); Status = "Relatório JSON exportado."; }
                break;
            case "BatchHistory": await RefreshHistoryAsync(ct); break;
            case "BatchOpenHistory":
                _batch = await _store.LoadAsync((SelectedHistory ?? throw new InvalidOperationException("Selecione um lote no histórico.")).Id, ct);
                Folder = _batch.Folder;
                LoadOptions(); Refresh(); Status = "Relatório persistido aberto. Retomar continua somente o trabalho incompleto; erros e pendências podem ser reprocessados explicitamente.";
                ReportReady?.Invoke(this, EventArgs.Empty); break;
            case "BatchReprocess":
                var group = RequireGroup();
                await _review.ReprocessAsync(RequireBatch(), group, ct);
                await RunAsync(ct, group.Id); break;
            case "BatchSaveReview":
                var reviewed = RequireGroup();
                reviewed.Content = ReviewEditor.Content; reviewed.Url = ReviewEditor.Url;
                await _review.SaveReviewedAsync(RequireBatch(), reviewed, AllowSimilar, ct);
                Refresh(); Status = reviewed.Reason; break;
            case "BatchSaveDraft":
                var draft = RequireGroup();
                draft.Content = ReviewEditor.Content; draft.Url = ReviewEditor.Url;
                draft.Status = ImportItemStatus.NeedsReview; draft.ExtractionCompleted = true;
                draft.Reason = "Correções preservadas no relatório; aguarda validação e salvamento no registro.";
                await _store.SaveAsync(RequireBatch(), ct); Refresh(); Status = "Correções da pendência preservadas no histórico."; break;
            case "BatchDiscard": await _review.DiscardAsync(RequireBatch(), RequireGroup(), ct); Refresh(); break;
            case "BatchMove":
                await _review.MoveAsync(RequireBatch(), RequireSource().Id, RequireTarget().Id, ct); Refresh(); break;
            case "BatchSplit":
                await _review.SplitAsync(RequireBatch(), RequireGroup().Id, RequireSource().Id, ct); Refresh(); break;
            case "BatchMerge":
                await _review.MergeAsync(RequireBatch(), RequireGroup().Id, RequireTarget().Id, ct); Refresh(); break;
            case "BatchPreview":
                var input = await _discovery.LoadVerifiedAsync(RequireSource(), ct);
                await _desktop.PreviewAsync(input, ct); break;
            case "BatchOpenJob":
                if (RequireGroup().JobId is null && RequireGroup().ExistingJobId is null) throw new InvalidOperationException("Este grupo ainda não está vinculado a um registro.");
                break;
            default: throw new InvalidOperationException("Ação de lote desconhecida.");
        }
    }
    public async Task InitializeAsync(CancellationToken ct)
    {
        ApplyOptions(await _settings.LoadBatchOptionsAsync(ct));
        await RefreshHistoryAsync(ct);
    }
    public async Task RefreshHistoryAsync(CancellationToken ct)
    {
        var history = await _store.HistoryAsync(ct);
        History.Clear(); foreach (var entry in history) History.Add(entry);
    }
    public async Task SynchronizeJobAsync(JobInfo job, CancellationToken ct)
    {
        await _review.SynchronizeJobAsync(job, ct);
        if (_batch is not null && _batch.Groups.Any(g => g.JobId == job.Id))
        { _batch = await _store.LoadAsync(_batch.Id, ct); Refresh(); }
    }
    private async Task DiscoverAsync(CancellationToken ct)
    {
        Status = "Descobrindo e validando imagens…"; Indeterminate = true;
        try
        {
            var options = Options();
            await _settings.SaveBatchOptionsAsync(options, ct);
            var found = await _discovery.DiscoverAsync(Folder, options, ct);
            Guid? existing = await _store.FindAsync(found.Fingerprint, ct);
            if (existing is { } id)
            {
                _batch = await _store.LoadAsync(id, ct);
                // Reattach renamed files by content, preserving the established partition and analysis checkpoints.
                var paths = found.Images.Where(i => i.Hash.Length > 0).GroupBy(i => i.Hash).ToDictionary(g => g.Key, g => new Queue<BatchScreenshot>(g));
                foreach (var image in _batch.Images)
                    if (paths.TryGetValue(image.Hash, out var queue) && queue.TryDequeue(out var replacement))
                    { image.Path = replacement.Path; image.Name = replacement.Name; }
                _batch.Folder = found.Folder;
                LoadOptions();
                Status = "Este conteúdo já possui histórico. Iniciar / retomar preserva os resultados concluídos.";
            }
            else { _batch = found; Status = "Pasta validada. Confira e ajuste a ordem; clique em Iniciar importação para processar."; }
            Refresh(); await RefreshHistoryAsync(ct);
        }
        finally { Indeterminate = false; }
    }
    private async Task RunAsync(CancellationToken ct, Guid? onlyGroup = null)
    {
        var batch = RequireBatch();
        var options = Options();
        await _settings.SaveBatchOptionsAsync(options, ct);
        var ollama = await _settings.LoadAsync(ct);
        bool running = true;
        IsRunning = true; _progressSummary = batch.Summary; ProcessingGroups.Clear();
        var progress = new Progress<BatchProgress>(value =>
        {
            if (!running) return;
            Status = value.Display; Indeterminate = value.Indeterminate;
            Completed = value.Completed; Total = value.Total;
            _progressSummary = value.Summary; Raise(nameof(Summary));
            for (int index = 0; index < value.GroupLines.Count; index++)
            {
                if (index >= ProcessingGroups.Count) ProcessingGroups.Add(value.GroupLines[index]);
                else if (ProcessingGroups[index] != value.GroupLines[index]) ProcessingGroups[index] = value.GroupLines[index];
            }
        });
        try { await _coordinator.RunAsync(batch, options, ollama, progress, ct, onlyGroup); }
        finally
        {
            running = false;
            IsRunning = false;
            Indeterminate = false; Refresh(); await RefreshHistoryAsync(CancellationToken.None);
            Status = batch.Status == ImportBatchStatus.Cancelled ? "Cancelada. Relatório parcial salvo; o lote pode ser retomado." : "Relatório atualizado. Confira os resultados abaixo.";
            ReportReady?.Invoke(this, EventArgs.Empty);
        }
    }
    private BatchImportOptions Options()
    {
        static int Parse(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number : throw new ArgumentException("Os limites do lote devem ser números inteiros.");
        var options = new BatchImportOptions
        {
            MaxFiles = Parse(MaxFiles),
            MaxImagesInMemory = Parse(MaxImagesInMemory),
            KeepAliveSeconds = Parse(KeepAliveSeconds)
        };
        options.Validate(); return options;
    }
    private void LoadOptions() => ApplyOptions(RequireBatch().Options);
    private void ApplyOptions(BatchImportOptions options)
    {
        MaxFiles = options.MaxFiles.ToString(CultureInfo.InvariantCulture);
        MaxImagesInMemory = Math.Max(2, options.MaxImagesInMemory).ToString(CultureInfo.InvariantCulture); KeepAliveSeconds = options.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
        Raise(nameof(MaxFiles)); Raise(nameof(MaxImagesInMemory)); Raise(nameof(KeepAliveSeconds));
    }
    private void Move(int delta)
    {
        var batch = RequireBatch();
        if (batch.Groups.Count > 0) throw new InvalidOperationException("O lote já possui grupos. Ajuste a composição no relatório.");
        var selected = SelectedImage ?? throw new InvalidOperationException("Selecione uma imagem.");
        int index = batch.Images.IndexOf(selected), target = index + delta;
        if (target < 0 || target >= batch.Images.Count) return;
        batch.Images.RemoveAt(index); batch.Images.Insert(target, selected);
        for (int i = 0; i < batch.Images.Count; i++) batch.Images[i].Order = i;
        Refresh(); SelectedImage = selected;
    }
    private void Refresh()
    {
        Images.Clear(); if (_batch is not null) foreach (var image in _batch.Images.OrderBy(i => i.Order)) Images.Add(image);
        RefreshGroups(); LoadGroup();
        Raise(nameof(Summary)); Raise(nameof(Notices)); Raise(nameof(ImageCount)); Raise(nameof(CanReorder));
    }
    private void RefreshGroups()
    {
        Guid? selected = SelectedGroup?.Id;
        AllGroups.Clear(); if (_batch is not null) foreach (var group in _batch.Groups.OrderBy(g => g.Order)) AllGroups.Add(group);
        FilterGroups();
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == selected) ?? Groups.FirstOrDefault();
    }
    private void FilterGroups()
    {
        var groups = AllGroups.Where(g => Filter switch
        {
            "Salvas" => g.Status == ImportItemStatus.Imported,
            "Duplicadas" => g.Status == ImportItemStatus.Duplicate,
            "Pendentes" => g.Status is ImportItemStatus.NeedsReview or ImportItemStatus.Pending or ImportItemStatus.Processing,
            "Com erro" => g.Status == ImportItemStatus.Failed,
            _ => true
        }).ToArray();
        Groups.Clear(); foreach (var group in groups) Groups.Add(group);
    }
    private void LoadGroup()
    {
        var editor = new EditorViewModel();
        if (SelectedGroup?.Content is { } content) editor.Fill(content);
        editor.Url = SelectedGroup?.Url ?? ""; ReviewEditor = editor; AllowSimilar = false;
        SourceImages.Clear(); if (_batch is not null && SelectedGroup is { } group) foreach (var image in _batch.ImagesFor(group)) SourceImages.Add(image);
        SelectedSource = SourceImages.FirstOrDefault();
        Raise(nameof(GroupDetails)); Raise(nameof(OriginalText));
    }
    private ImportBatch RequireBatch() => _batch ?? throw new InvalidOperationException("Selecione uma pasta ou abra um lote no histórico.");
    private DetectedJobGroup RequireGroup() => SelectedGroup ?? throw new InvalidOperationException("Selecione um grupo no relatório.");
    private DetectedJobGroup RequireTarget() => Target ?? throw new InvalidOperationException("Selecione o grupo de destino.");
    private BatchScreenshot RequireSource() => SelectedSource ?? throw new InvalidOperationException("Selecione uma captura de origem.");
}