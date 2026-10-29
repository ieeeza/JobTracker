using System.Collections.ObjectModel;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;

namespace JobTracker.Presentation.ViewModels;

using JobTracker.Modules.JobAnalysis.Application.Services;
using Microsoft.Extensions.Logging;

public sealed partial class JobEditorViewModel : ObservableModel, IDisposable
{
    private readonly TrackingService _tracking;
    private readonly JobIdentifier _identifier;
    private readonly DesktopServices _desktop;
    private readonly JobWorkspace _workspace;
    private readonly UiOperationState _operations;
    private readonly ILogger<JobEditorViewModel> _logger;
    private JobInfo? _editing;
    private EditorViewModel _editor = new();
    private EditorSnapshot? _savedEditor;
    private string? _canonical;
    private string _evidence = "Cadastro manual: preencha os campos obrigatórios.";
    private string _appointmentTitle = "Entrevista", _appointmentTime = "09:00", _appointmentKind = "Entrevista", _appointmentNotes = "";
    private DateTime? _appointmentDate = DateTime.Today;

    private sealed record EditorSnapshot(string Url, JobTracker.Modules.JobAnalysis.Domain.JobContent Content, ApplicationStatus Status, DateTime? ApplicationDate, string? Canonical, string Evidence, JobTracker.Modules.JobAnalysis.Domain.OcrCapture? Capture, int ImageRevision);
    private EditorSnapshot CurrentEditor => new(Editor.Url, Editor.Content, Editor.Status, Editor.ApplicationDate, _canonical, Evidence, Images.Capture, Images.Revision);
    public bool HasUnsavedChanges => _savedEditor != CurrentEditor;

    public JobEditorViewModel(TrackingService tracking, JobIdentifier identifier, DesktopServices desktop,
        JobWorkspace workspace, UiOperationState operations, ILogger<JobEditorViewModel> logger,
        ImageImportViewModel images, OllamaSettingsViewModel ollama, BatchImportViewModel batch)
    {
        _tracking = tracking; _identifier = identifier; _desktop = desktop; _workspace = workspace;
        _operations = operations; _logger = logger; Images = images; Ollama = ollama; Batch = batch;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        workspace.Reloaded += OnReloaded;
        operations.PropertyChanged += OnOperationChanged;
        ollama.PropertyChanged += OnOllamaChanged;
        SaveCommand = operations.Command(nameof(SaveCommand), SaveAsync, canExecute: () => !IsFolderImport);
        AnalyzeImagesCommand = operations.Command(nameof(AnalyzeImagesCommand), AnalyzeImagesAsync, "Cancelar análise");
        CheckDuplicatesCommand = operations.Command(nameof(CheckDuplicatesCommand), RefreshDuplicatesAsync);
        OpenDuplicateCommand = operations.Command(nameof(OpenDuplicateCommand), _ => { OpenDuplicate(); return Task.CompletedTask; });
        OpenUrlCommand = operations.Command(nameof(OpenUrlCommand), _ => { desktop.OpenUrl(Editor.Url); return Task.CompletedTask; });
        CopyCommand = operations.Command(nameof(CopyCommand), _ => { desktop.Copy($"{Editor.Title}\n{Editor.Company}\n{Editor.Url}\n{Editor.Description}"); return Task.CompletedTask; });
        CopyUrlCommand = operations.Command(nameof(CopyUrlCommand), _ => { desktop.Copy(Editor.Url); return Task.CompletedTask; });
        DeleteCommand = operations.Command(nameof(DeleteCommand), DeleteAsync);
        ScheduleCommand = operations.Command(nameof(ScheduleCommand), ScheduleAsync);
    }

    public event EventHandler? Opened;
    public event EventHandler? Created;
    public event EventHandler? Deleted;
    public AsyncUiCommand SaveCommand { get; }
    public AsyncUiCommand AnalyzeImagesCommand { get; }
    public AsyncUiCommand CheckDuplicatesCommand { get; }
    public AsyncUiCommand OpenDuplicateCommand { get; }
    public AsyncUiCommand OpenUrlCommand { get; }
    public AsyncUiCommand CopyCommand { get; }
    public AsyncUiCommand CopyUrlCommand { get; }
    public AsyncUiCommand DeleteCommand { get; }
    public AsyncUiCommand ScheduleCommand { get; }
    public BatchImportViewModel Batch { get; }
    public UiOperationState Operations => _operations;
    public bool IsBusy => _operations.IsBusy;
    public bool IsIdle => _operations.IsIdle;
    public IReadOnlyList<StatusOption> Statuses => StatusCatalog.All;
    public EditorViewModel Editor
    {
        get => _editor;
        private set
        {
            _editor.PropertyChanged -= OnEditorPropertyChanged;
            Set(ref _editor, value);
            _editor.PropertyChanged += OnEditorPropertyChanged;
        }
    }
    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A canonical address belongs to the analyzed URL, never to a later manual replacement.
        if (e.PropertyName == nameof(EditorViewModel.Url)) _canonical = null;
    }
    public bool IsExisting => _editing is not null;
    public string Evidence { get => _evidence; private set => Set(ref _evidence, value); }
    public IReadOnlyList<EventInfo> History => _editing?.History ?? [];
    public IReadOnlyList<AppointmentInfo> JobAppointments => _editing?.Appointments ?? [];
    public string AppointmentTitle { get => _appointmentTitle; set => Set(ref _appointmentTitle, value); }
    public string AppointmentTime { get => _appointmentTime; set => Set(ref _appointmentTime, value); }
    public string AppointmentKind { get => _appointmentKind; set => Set(ref _appointmentKind, value); }
    public string AppointmentNotes { get => _appointmentNotes; set => Set(ref _appointmentNotes, value); }
    public DateTime? AppointmentDate { get => _appointmentDate; set => Set(ref _appointmentDate, value); }

    private void OnOperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UiOperationState.IsBusy)) return;
        Raise(nameof(IsBusy)); Raise(nameof(IsIdle));
        if (IsIdle && _focusEditorWhenIdle)
        {
            _focusEditorWhenIdle = false;
            EditorFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnOllamaChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OllamaSettingsViewModel.Configuration)) Images.OllamaOptions = Ollama.Configuration;
    }

    private void OnReloaded(object? sender, EventArgs e)
    {
        if (_editing is not { } edit) return;
        _editing = _workspace.All.SingleOrDefault(j => j.Id == edit.Id);
        Raise(nameof(IsExisting)); Raise(nameof(History)); Raise(nameof(JobAppointments));
    }

    public bool ConfirmDiscard() => !HasUnsavedChanges ||
        _desktop.Confirm("Sair do formulário", "Alterações ainda não salvas serão descartadas. Deseja continuar?");

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (_editing is not null && HasUnsavedChanges &&
            !_desktop.Confirm("Recarregar detalhes", "Recarregar os dados persistidos e descartar alterações não salvas no formulário?")) return;
        await _workspace.ReloadAsync(ct);
        if (_editing is not null) Open(_editing);
    }

    public void ResetAfterRestore() => NewEditor();

    private void OpenDuplicate()
    {
        var match = SelectedDuplicate ?? throw new InvalidOperationException("Selecione uma correspondência.");
        if (!HasUnsavedChanges || _desktop.Confirm("Abrir histórico", "Abrir o registro existente? A prévia e os prints não salvos serão descartados."))
            Open(match.Job);
    }

    private async Task DeleteAsync(CancellationToken ct)
    {
        var job = _editing ?? throw new InvalidOperationException("Selecione uma vaga.");
        if (!_desktop.Confirm("Mover para a lixeira", $"Mover {job.Content.Title} para a lixeira? O histórico poderá ser recuperado.")) return;
        await _tracking.DeleteAsync(job, true, ct);
        NewEditor();
        Deleted?.Invoke(this, EventArgs.Empty);
        await _workspace.ReloadAsync(ct);
    }

    private async Task ScheduleAsync(CancellationToken ct)
    {
        if (_editing is null) throw new InvalidOperationException("Salve a vaga antes de agendar.");
        if (AppointmentDate is not { } day || !TimeOnly.TryParseExact(AppointmentTime, "HH:mm", out var time))
            throw new ArgumentException("Informe uma data e um horário no formato HH:mm.");
        await _tracking.ScheduleAsync(_editing, AppointmentTitle, day.Date.Add(time.ToTimeSpan()), AppointmentKind, AppointmentNotes, ct);
        await _workspace.ReloadAsync(ct);
    }

    public void NewEditor()
    {
        ResetImport();
        _editing = null; _canonical = null; Editor = new(); Evidence = "Cargo e empresa são obrigatórios. Revise os dados antes de salvar.";
        AppointmentTitle = "Entrevista"; AppointmentDate = DateTime.Today; AppointmentTime = "09:00";
        AppointmentKind = "Entrevista"; AppointmentNotes = "";
        _savedEditor = CurrentEditor;
        Raise(nameof(IsExisting)); Raise(nameof(History)); Raise(nameof(JobAppointments));
    }
    public void Open(JobInfo job)
    {
        _screenshotModeIndex = 0;
        Raise(nameof(ScreenshotModeIndex)); Raise(nameof(IsFolderImport)); Raise(nameof(IsSingleJobForm));
        ResetImport(); Images.Reset(job.OcrCapture);
        _editing = job; Editor = new() { Url = job.Url, Status = job.Status, ApplicationDate = job.AppliedAt?.ToLocalTime().Date ?? DateTime.Today };
        Editor.Fill(job.Content); _canonical = job.CanonicalUrl; Evidence = job.ExtractionMetadata;
        _savedEditor = CurrentEditor;
        Opened?.Invoke(this, EventArgs.Empty); Raise(nameof(IsExisting)); Raise(nameof(History)); Raise(nameof(JobAppointments));
    }
    private async Task SaveAsync(CancellationToken ct)
    {
        Images.Capture?.Validate();
        await RefreshDuplicatesAsync(ct);
        if (DuplicateMatches.Any(m => m.Kind == DuplicateKind.Confirmed))
            throw new InvalidOperationException("Esta vaga já está cadastrada. Selecione a correspondência e abra seu histórico.");
        DateTime? applied = Editor.ApplicationDate is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime() : null;
        if (Editor.Status is not (ApplicationStatus.Saved or ApplicationStatus.Archived) && applied is null) throw new ArgumentException("Informe a data da candidatura.");
        bool creating = _editing is null;
        Guid id;
        if (_editing is { } existing)
        { await _tracking.EditAsync(existing, Editor.Content, Editor.Status, applied, _canonical, Evidence, ct, Images.Capture, url: Editor.Url); id = existing.Id; }
        else
        {
            string? importSource = Images.HasAnalysis ? "Prints (IA local)" : null;
            try { id = await _tracking.CreateAsync(Editor.Url, Editor.Content, _canonical, Evidence, Editor.Status, applied, false, ct, Images.Capture, importSource); }
            catch (SimilarJobException ex)
            {
                if (!_desktop.Confirm("Possível duplicidade", string.Join("\n\n", ex.Matches.Select(m => m.Summary)) + "\n\nConfirma que esta é uma oportunidade diferente?"))
                { _operations.Message = "Cadastro cancelado. Revise os dados preservados no formulário."; return; }
                id = await _tracking.CreateAsync(Editor.Url, Editor.Content, _canonical, Evidence, Editor.Status, applied, true, ct, Images.Capture, importSource);
            }
        }
        // The repository has confirmed the commit. Refresh failures must never invite a second insertion.
        if (creating)
        {
            NewEditor(); Created?.Invoke(this, EventArgs.Empty); _focusEditorWhenIdle = true;
            _operations.Message = "Vaga salva. Tudo pronto para cadastrar a próxima.";
        }
        else _operations.Message = "Alterações salvas.";
        try
        {
            await _workspace.ReloadAsync(CancellationToken.None);
            if (!creating)
            {
                var updated = _workspace.Find(id);
                Open(updated);
                await Batch.SynchronizeJobAsync(updated, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Vaga persistida, mas a atualização da interface falhou: {ErrorType}; {Stack}", ex.GetType().Name, ex.StackTrace);
            _operations.Message = "Vaga salva no banco. Não foi possível atualizar os painéis; use Atualizar antes de editar esse registro novamente.";
        }
    }

    public void Dispose()
    {
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _workspace.Reloaded -= OnReloaded;
        _operations.PropertyChanged -= OnOperationChanged;
        Ollama.PropertyChanged -= OnOllamaChanged;
    }
}
