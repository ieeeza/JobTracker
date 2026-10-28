using System.Collections.ObjectModel;
using System.IO;
using JobTracker.Modules.Dashboard.Application;
using JobTracker.Modules.JobAnalysis.Application.Exceptions;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Modules.Settings.Application;
using Microsoft.Extensions.Logging;

namespace JobTracker.Presentation;

public sealed partial class ShellViewModel : ObservableModel, IDisposable
{
    private readonly TrackingService _tracking;
    private readonly JobIdentifier _identifier;
    private readonly IPreferences _preferences;
    private readonly IDatabaseMaintenance _database;
    private readonly DataTransferService _transfer;
    private readonly DesktopServices _desktop;
    private readonly ILogger<ShellViewModel> _logger;
    private CancellationTokenSource? _operation;
    private Task _runningTask = Task.CompletedTask;
    private IReadOnlyList<JobInfo> _all = [];
    private JobInfo? _editing, _selectedJob;
    private EditorViewModel _editor = new();
    private EditorSnapshot? _savedEditor;
    private sealed record EditorSnapshot(string Url, JobTracker.Modules.JobAnalysis.Domain.JobContent Content, ApplicationStatus Status, DateTime? ApplicationDate, string? Canonical, string Evidence, JobTracker.Modules.JobAnalysis.Domain.OcrCapture? Capture, int ImageRevision);
    private EditorSnapshot CurrentEditor => new(Editor.Url, Editor.Content, Editor.Status, Editor.ApplicationDate, _canonical, Evidence, Images.Capture, Images.Revision);
    private bool HasUnsavedChanges => Page is "Nova vaga" or "Detalhes" && _savedEditor != CurrentEditor;
    private string _page = "Dashboard", _message = "Tudo pronto. Seus dados ficam neste computador.", _search = "", _company = "", _technology = "", _source = "", _role = "";
    private string _sort = "Mais recentes", _evidence = "Cadastro manual: preencha os campos obrigatórios.";
    private string? _canonical;
    private bool _busy, _cards, _dark, _reduceMotion, _includeCompleted;
    private DateTime? _from, _to, _agendaFrom = DateTime.Today, _agendaTo;
    private StatusOption? _statusFilter;
    private DashboardSummary _dashboard = DashboardSummary.From([]);
    private AppointmentInfo? _selectedAppointment;
    private string _appointmentTitle = "Entrevista", _appointmentTime = "09:00", _appointmentKind = "Entrevista", _appointmentNotes = "";
    private DateTime? _appointmentDate = DateTime.Today;
    private ApplicationStatus _targetStatus = ApplicationStatus.Applied;
    public ShellViewModel(TrackingService tracking,
        JobIdentifier identifier, IPreferences preferences, IDatabaseMaintenance database, DataTransferService transfer,
        DesktopServices desktop, ILogger<ShellViewModel> logger, ImageImportViewModel images,
        OllamaSettingsViewModel ollama, BatchImportViewModel batch)
    {
        _tracking = tracking; _identifier = identifier;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _preferences = preferences; _database = database; _transfer = transfer; _desktop = desktop; _logger = logger; Images = images;
        Ollama = ollama;
        Batch = batch;
        Batch.ActionRequested += OnBatchAction;
        Batch.PropertyChanged += OnBatchPropertyChanged;
        Action = new UiCommand(action => _runningTask = RunAsync(action), () => !IsBusy);
        Navigate = new UiCommand(NavigateTo, () => !IsBusy);
        Cancel = new UiCommand(_ => _operation?.Cancel(), () => IsBusy);
    }
    public UiCommand Action { get; }
    public UiCommand Navigate { get; }
    public UiCommand Cancel { get; }
    public BatchImportViewModel Batch { get; }
    private void OnBatchAction(string? action) => _runningTask = RunAsync(action);
    private void OnBatchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (IsBusy && e.PropertyName == nameof(BatchImportViewModel.Status)) Message = Batch.Status;
    }
    private string _cancelLabel = "Cancelar";
    public string CancelLabel { get => _cancelLabel; private set => Set(ref _cancelLabel, value); }
    public string Page { get => _page; private set { Set(ref _page, value); Raise(nameof(PageDescription)); Raise(nameof(IsTrash)); } }
    public string PageDescription => Page switch
    {
        "Dashboard" => "Uma visão real da sua busca pelo próximo passo.",
        "Vagas" => "Pesquise, filtre e organize oportunidades.", "Nova vaga" => "Escolha como preencher, revise e salve. O próximo cadastro fica pronto automaticamente.",
        "Detalhes" => "Edite os dados, acompanhe o processo e planeje sua próxima ação.", "Candidaturas" => "Seu processo, etapa por etapa. Selecione uma vaga para alterar o status.",
        "Agenda" => "Entrevistas, prazos e lembretes no horário local.", "Lixeira" => "Os registros permanecem recuperáveis e não entram nas métricas.",
        _ => "Aparência, captura e controle dos seus dados locais."
    };
    public bool IsTrash => Page == "Lixeira";
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            Set(ref _busy, value); Raise(nameof(IsIdle)); Action.Refresh(); Navigate.Refresh(); Cancel.Refresh();
            Batch.IsBusy = value;
            if (!value && _focusEditorWhenIdle) { _focusEditorWhenIdle = false; EditorFocusRequested?.Invoke(this, EventArgs.Empty); }
        }
    }
    public bool IsIdle => !IsBusy;
    public string Message { get => _message; private set => Set(ref _message, value); }
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
    public JobInfo? SelectedJob { get => _selectedJob; set { if (value is not null || Page != "Candidaturas") Set(ref _selectedJob, value); } }
    public ObservableCollection<JobInfo> Jobs { get; } = [];
    public ObservableCollection<BoardColumn> Board { get; } = [];
    public ObservableCollection<AppointmentInfo> Agenda { get; } = [];
    public DashboardSummary Dashboard { get => _dashboard; private set => Set(ref _dashboard, value); }
    public string ResultsLabel => Jobs.Count == 0 ? "Nenhuma vaga encontrada. Cadastre uma oportunidade ou ajuste os filtros." : $"{Jobs.Count} vaga(s) encontrada(s)";
    public string AgendaLabel => Agenda.Count == 0 ? "Nenhum compromisso neste período." : $"{Agenda.Count} compromisso(s)";
    public string Search { get => _search; set { if (Set(ref _search, value)) Filter(); } }
    public string CompanyFilter { get => _company; set { if (Set(ref _company, value)) Filter(); } }
    public string TechnologyFilter { get => _technology; set { if (Set(ref _technology, value)) Filter(); } }
    public string SourceFilter { get => _source; set { if (Set(ref _source, value)) Filter(); } }
    public string RoleFilter { get => _role; set { if (Set(ref _role, value)) Filter(); } }
    public DateTime? From { get => _from; set { if (Set(ref _from, value)) Filter(); } }
    public DateTime? To { get => _to; set { if (Set(ref _to, value)) Filter(); } }
    public StatusOption? StatusFilter { get => _statusFilter; set { if (Set(ref _statusFilter, value)) Filter(); } }
    public IReadOnlyList<StatusOption> Statuses => StatusCatalog.All;
    public IReadOnlyList<string> Sorts { get; } = ["Mais recentes", "Mais antigas", "Empresa", "Cargo", "Candidatura"];
    public string Sort { get => _sort; set { if (Set(ref _sort, value)) Filter(); } }
    public bool Cards { get => _cards; set => Set(ref _cards, value); }
    public bool Dark { get => _dark; set => Set(ref _dark, value); }
    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }
    public string DatabasePath => _database.DatabasePath;
    public DateTime? AgendaFrom { get => _agendaFrom; set { if (Set(ref _agendaFrom, value)) FilterAgenda(); } }
    public DateTime? AgendaTo { get => _agendaTo; set { if (Set(ref _agendaTo, value)) FilterAgenda(); } }
    public bool IncludeCompleted { get => _includeCompleted; set { if (Set(ref _includeCompleted, value)) FilterAgenda(); } }
    public AppointmentInfo? SelectedAppointment { get => _selectedAppointment; set => Set(ref _selectedAppointment, value); }
    public string AppointmentTitle { get => _appointmentTitle; set => Set(ref _appointmentTitle, value); }
    public string AppointmentTime { get => _appointmentTime; set => Set(ref _appointmentTime, value); }
    public string AppointmentKind { get => _appointmentKind; set => Set(ref _appointmentKind, value); }
    public string AppointmentNotes { get => _appointmentNotes; set => Set(ref _appointmentNotes, value); }
    public DateTime? AppointmentDate { get => _appointmentDate; set => Set(ref _appointmentDate, value); }
    public ApplicationStatus TargetStatus { get => _targetStatus; set => Set(ref _targetStatus, value); }

    public Task InitializeAsync() => _runningTask = RunAsync("Initialize");
    public bool ConfirmClose() => _desktop.Confirm("Fechar JobTracker", IsBusy
        ? "Há uma operação em andamento. Deseja cancelá-la e fechar após liberar os recursos?"
        : "As alterações não salvas no formulário serão descartadas. Deseja fechar?");
    public bool NeedsCloseConfirmation => IsBusy || HasUnsavedChanges;
    public async Task CancelAndWaitAsync() { _operation?.Cancel(); await _runningTask; }
    private void NavigateTo(string? page)
    {
        if (page is null || IsBusy) return;
        if (HasUnsavedChanges && (page != Page || page == "Nova vaga") && !_desktop.Confirm("Sair do formulário", "Alterações ainda não salvas serão descartadas. Deseja continuar?")) return;
        if (page == "Nova vaga") NewEditor();
        Page = page; Filter();
        if (page == "Nova vaga") EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }
    private void NewEditor()
    {
        ResetImport();
        _editing = null; _canonical = null; Editor = new(); Evidence = "Cargo e empresa são obrigatórios. Revise os dados antes de salvar.";
        AppointmentTitle = "Entrevista"; AppointmentDate = DateTime.Today; AppointmentTime = "09:00";
        AppointmentKind = "Entrevista"; AppointmentNotes = "";
        _savedEditor = CurrentEditor;
        Raise(nameof(IsExisting)); Raise(nameof(History)); Raise(nameof(JobAppointments));
    }
    private void Open(JobInfo job)
    {
        _screenshotModeIndex = 0;
        Raise(nameof(ScreenshotModeIndex)); Raise(nameof(IsFolderImport)); Raise(nameof(IsSingleJobForm));
        ResetImport(); Images.Reset(job.OcrCapture);
        _editing = job; Editor = new() { Url = job.Url, Status = job.Status, ApplicationDate = job.AppliedAt?.ToLocalTime().Date ?? DateTime.Today };
        Editor.Fill(job.Content); _canonical = job.CanonicalUrl; Evidence = job.ExtractionMetadata;
        _savedEditor = CurrentEditor;
        Page = "Detalhes"; Raise(nameof(IsExisting)); Raise(nameof(History)); Raise(nameof(JobAppointments));
    }
    private async Task RunAsync(string? action, IReadOnlyList<string>? imagePaths = null)
    {
        if (IsBusy || action is null) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation; IsBusy = true;
        CancelLabel = action.StartsWith("Batch", StringComparison.Ordinal) ? "Cancelar importação" : action == "ImageAnalyze" ? "Cancelar análise" : "Cancelar";
        ImportMessage = "";
        try
        {
            Message = "Processando…";
            if (action == "ImageDrop") await Images.AddFilesAsync(imagePaths, cancellation.Token);
            else await ExecuteAsync(action, cancellation.Token);
            if (Message == "Processando…") Message = "Operação concluída.";
        }
        catch (OperationCanceledException) { Message = "Operação cancelada. Os dados do formulário foram preservados. Atualize a lista se a gravação já havia começado."; ImportMessage = Message; }
        catch (Exception ex)
        {
            _logger.LogError("Operação {Operation} falhou: {ErrorType}. Local: {Stack}", action, ex.GetType().Name, ex.StackTrace);
            Message = ex switch
            {
                Microsoft.Data.Sqlite.SqliteException => "Não foi possível acessar o SQLite. Verifique espaço, permissões e se outro programa está usando o banco.",
                Microsoft.EntityFrameworkCore.DbUpdateException => "A gravação falhou. Atualize os dados e verifique o banco local.",
                ArgumentException or InvalidOperationException or JobPersistenceException or DuplicateJobException or TimeoutException or InvalidDataException => ex.Message,
                System.Text.Json.JsonException => "O arquivo JSON não corresponde ao formato de exportação do JobTracker. Nenhum registro foi importado.",
                IOException or UnauthorizedAccessException => "Não foi possível acessar o arquivo. Verifique o caminho, as permissões e o espaço disponível.",
                _ => "A operação falhou. Seus dados não foram descartados; consulte o log de diagnóstico na pasta local."
            };
            ImportMessage = Message;
        }
        finally { _operation = null; IsBusy = false; CancelLabel = "Cancelar"; }
    }
    private async Task ExecuteAsync(string action, CancellationToken ct)
    {
        if (action.StartsWith("Batch", StringComparison.Ordinal))
        {
            await Batch.ExecuteAsync(action, ct);
            if (action == "BatchOpenJob")
            {
                await ReloadAsync(ct);
                Guid id = Batch.SelectedGroup?.JobId ?? Batch.SelectedGroup?.ExistingJobId ?? throw new InvalidOperationException("Registro não encontrado.");
                Open(_all.Single(j => j.Id == id));
            }
            else if (action is "BatchStart" or "BatchSaveReview") await ReloadAsync(CancellationToken.None);
            Message = Batch.Status;
            return;
        }
        switch (action)
        {
            case "Initialize":
                Dark = await _preferences.GetAsync("DarkTheme", ct) == "true";
                ReduceMotion = await _preferences.GetAsync("ReduceMotion", ct) == "true";
                await InitializeOllamaAsync(ct);
                await Batch.InitializeAsync(ct);
                _desktop.SetTheme(Dark, ReduceMotion); await ReloadAsync(ct); break;
            case "Refresh":
                if (_editing is not null && HasUnsavedChanges && !_desktop.Confirm("Recarregar detalhes", "Recarregar os dados persistidos e descartar alterações não salvas no formulário?")) break;
                await ReloadAsync(ct);
                if (_editing is not null && Page == "Detalhes") Open(_editing);
                break;
            case "ClearFilters":
                Search = CompanyFilter = TechnologyFilter = SourceFilter = RoleFilter = "";
                From = To = null; StatusFilter = null; break;
            case "Open": Open(SelectedJob ?? throw new InvalidOperationException("Selecione uma vaga para ver os detalhes.")); break;
            case "ImageAdd": await Images.AddFilesAsync(null, ct); break;
            case "ImagePaste": await Images.PasteAsync(ct); break;
            case "ImageAnalyze": await AnalyzeImagesAsync(ct); break;
            case "OllamaVerify": await Ollama.VerifyAsync(Page != "Configurações", ct); break;
            case "OllamaSave":
                await Ollama.SaveAsync(ct); Images.OllamaOptions = Ollama.Configuration;
                Message = "Configuração Ollama salva neste computador."; break;
            case "ImageRemove": Images.RemoveSelected(); break;
            case "ImageClear": Images.Clear(); break;
            case "ImageUp": Images.Move(-1); break;
            case "ImageDown": Images.Move(1); break;
            case "ImagePreview": await Images.PreviewAsync(ct); break;
            case "CheckDuplicates": await RefreshDuplicatesAsync(ct); break;
            case "OpenDuplicate":
                if (SelectedDuplicate is not { } match) throw new InvalidOperationException("Selecione uma correspondência.");
                if (!HasUnsavedChanges || _desktop.Confirm("Abrir histórico", "Abrir o registro existente? A prévia e os prints não salvos serão descartados.")) Open(match.Job);
                break;
            case "Save": if (!IsFolderImport && Page is ("Nova vaga" or "Detalhes")) await SaveAsync(ct); break;
            case "OpenUrl": _desktop.OpenUrl(Editor.Url); break;
            case "Copy": _desktop.Copy($"{Editor.Title}\n{Editor.Company}\n{Editor.Url}\n{Editor.Description}"); break;
            case "CopyUrl": _desktop.Copy(Editor.Url); break;
            case "Delete":
                var deleted = (Page == "Detalhes" ? _editing : SelectedJob) ?? throw new InvalidOperationException("Selecione uma vaga.");
                if (_desktop.Confirm("Mover para a lixeira", $"Mover {deleted.Content.Title} para a lixeira? O histórico poderá ser recuperado."))
                { await _tracking.DeleteAsync(deleted, true, ct); _editing = null; Page = "Vagas"; await ReloadAsync(ct); }
                break;
            case "Recover":
                await _tracking.DeleteAsync(SelectedJob ?? throw new InvalidOperationException("Selecione uma vaga da lixeira."), false, ct);
                await ReloadAsync(ct); break;
            case "Status":
                var job = SelectedJob ?? throw new InvalidOperationException("Selecione uma vaga no quadro.");
                if (_desktop.Confirm("Alterar status", $"{job.Content.Title}\n\nAlterar para {StatusCatalog.Label(TargetStatus)}? A alteração será registrada no histórico."))
                { await _tracking.StatusAsync(job, TargetStatus, null, ct); await ReloadAsync(ct); }
                break;
            case "ArchiveFiltered":
                var targets = Jobs.Where(j => !j.IsDeleted && j.Status != ApplicationStatus.Archived).ToArray();
                if (targets.Length == 0) throw new InvalidOperationException("Não há vagas para arquivar neste filtro.");
                if (_desktop.Confirm("Arquivar resultado do filtro", $"Arquivar {targets.Length} vagas exibidas? O histórico permanece disponível. A operação pode ser interrompida entre registros."))
                {
                    int completed = 0;
                    try { foreach (var target in targets) { await _tracking.StatusAsync(target, ApplicationStatus.Archived, null, ct); completed++; } }
                    finally { Message = $"{completed} de {targets.Length} vagas arquivadas."; await ReloadAsync(CancellationToken.None); }
                }
                break;
            case "Schedule":
                if (_editing is null) throw new InvalidOperationException("Salve a vaga antes de agendar.");
                if (AppointmentDate is not { } day || !TimeOnly.TryParseExact(AppointmentTime, "HH:mm", out var time)) throw new ArgumentException("Informe uma data e um horário no formato HH:mm.");
                await _tracking.ScheduleAsync(_editing, AppointmentTitle, day.Date.Add(time.ToTimeSpan()), AppointmentKind, AppointmentNotes, ct);
                await ReloadAsync(ct); break;
            case "CompleteAppointment":
                var appointment = SelectedAppointment ?? throw new InvalidOperationException("Selecione um compromisso.");
                await _tracking.CompleteAsync(_all.Single(j => j.Id == appointment.JobId), appointment.Id, ct); await ReloadAsync(ct); break;
            case "OpenAppointment":
                Open(_all.Single(j => j.Id == (SelectedAppointment ?? throw new InvalidOperationException("Selecione um compromisso.")).JobId)); break;
            case "SaveSettings":
                await _preferences.SetManyAsync(new Dictionary<string, string>
                {
                    ["DarkTheme"] = Dark ? "true" : "false", ["ReduceMotion"] = ReduceMotion ? "true" : "false"
                }, ct);
                _desktop.SetTheme(Dark, ReduceMotion); break;
            case "Folder": _desktop.OpenFolder(DatabasePath); break;
            case "ExportJson": case "ExportCsv":
                string? export = _desktop.SavePath(action == "ExportCsv" ? "csv" : "json");
                if (export is not null) { await _transfer.ExportAsync(export, action == "ExportCsv", ct); Message = "Exportação concluída."; }
                break;
            case "Import": case "ImportCsv":
                string? input = _desktop.OpenPath(action == "ImportCsv" ? "csv" : "json");
                if (input is null) break;
                ImportPreview preview = await _transfer.PreviewAsync(input, action == "ImportCsv", ct);
                string report = preview.Summary + "\n\n" + string.Join("\n", preview.Lines.Select(l => $"{l.Number}. {l.Title} — {l.Result}"));
                if (preview.Accepted.Count > 0 && _desktop.Confirm("Prévia da importação", report + "\n\nImportar os registros aceitos, incluindo as oportunidades semelhantes indicadas?"))
                { await _transfer.ImportAsync(preview, ct); await ReloadAsync(ct); Message = "Importação concluída: " + preview.Summary; }
                else Message = "Nenhum dado importado. " + preview.Summary;
                if (preview.Accepted.Count == 0) _desktop.Confirm("Relatório da importação", report + "\n\nNenhum dado será importado.");
                break;
            case "Backup":
                string? destination = _desktop.SavePath("db");
                if (destination is not null) { await _database.BackupAsync(destination, ct); Message = "Backup consistente criado."; }
                break;
            case "Restore":
                string? source = _desktop.OpenPath("db");
                if (source is not null && _desktop.Confirm("Restaurar banco", "O banco selecionado substituirá todos os dados atuais, incluindo preferências. Um backup de segurança será criado na pasta local antes da substituição. Deseja continuar?"))
                { string safety = await _database.RestoreAsync(source, ct); _editing = null; await ExecuteAsync("Initialize", ct); Message = "Banco restaurado. Backup anterior: " + safety; }
                break;
            default: throw new InvalidOperationException("Ação desconhecida.");
        }
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
                { Message = "Cadastro cancelado. Revise os dados preservados no formulário."; return; }
                id = await _tracking.CreateAsync(Editor.Url, Editor.Content, _canonical, Evidence, Editor.Status, applied, true, ct, Images.Capture, importSource);
            }
        }
        // The repository has confirmed the commit. Refresh failures must never invite a second insertion.
        if (creating)
        {
            NewEditor(); Page = "Nova vaga"; _focusEditorWhenIdle = true;
            Message = "Vaga salva. Tudo pronto para cadastrar a próxima.";
        }
        else Message = "Alterações salvas.";
        try
        {
            await ReloadAsync(CancellationToken.None);
            if (!creating)
            {
                var updated = _all.Single(j => j.Id == id);
                Open(updated);
                await Batch.SynchronizeJobAsync(updated, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Vaga persistida, mas a atualização da interface falhou: {ErrorType}; {Stack}", ex.GetType().Name, ex.StackTrace);
            Message = "Vaga salva no banco. Não foi possível atualizar os painéis; use Atualizar antes de editar esse registro novamente.";
        }
    }
    private async Task ReloadAsync(CancellationToken ct)
    {
        _all = await _tracking.ListAsync(ct); Dashboard = DashboardSummary.From(_all);
        if (_editing is { } edit)
        {
            _editing = _all.SingleOrDefault(j => j.Id == edit.Id);
            Raise(nameof(History)); Raise(nameof(JobAppointments));
        }
        Filter(); FilterAgenda();
    }
    private void Filter()
    {
        static bool Contains(string text, string query) => text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        var query = _all.Where(j => j.IsDeleted == IsTrash &&
            (string.IsNullOrWhiteSpace(Search) || Contains(string.Join(" ", j.Content.Company, j.Content.Title, j.Content.Description, j.Content.Technologies), Search)) &&
            Contains(j.Content.Company, CompanyFilter) && Contains(j.Content.Title, RoleFilter) && Contains(j.Content.Technologies, TechnologyFilter) && Contains(j.Source, SourceFilter) &&
            (StatusFilter is null || j.Status == StatusFilter.Value) && (From is null || j.CreatedAt.ToLocalTime().Date >= From.Value.Date) && (To is null || j.CreatedAt.ToLocalTime().Date <= To.Value.Date));
        query = Sort switch { "Empresa" => query.OrderBy(j => j.Content.Company), "Cargo" => query.OrderBy(j => j.Content.Title),
            "Mais antigas" => query.OrderBy(j => j.CreatedAt), "Candidatura" => query.OrderByDescending(j => j.AppliedAt), _ => query.OrderByDescending(j => j.CreatedAt) };
        Guid? selected = SelectedJob?.Id;
        Jobs.Clear(); foreach (var job in query) Jobs.Add(job);
        Set(ref _selectedJob, Jobs.FirstOrDefault(j => j.Id == selected), nameof(SelectedJob));
        Board.Clear(); foreach (var status in Statuses) Board.Add(new(status.Label, Jobs.Where(j => j.Status == status.Value).ToArray()));
        Raise(nameof(ResultsLabel));
    }
    private void FilterAgenda()
    {
        Agenda.Clear();
        foreach (var item in _all.Where(j => !j.IsDeleted).SelectMany(j => j.Appointments).Where(a =>
            (IncludeCompleted || !a.Completed) && (AgendaFrom is null || a.LocalDateTime.Date >= AgendaFrom.Value.Date) &&
            (AgendaTo is null || a.LocalDateTime.Date <= AgendaTo.Value.Date)).OrderBy(a => a.LocalDateTime)) Agenda.Add(item);
        Raise(nameof(AgendaLabel));
    }
    public void Dispose() { Batch.ActionRequested -= OnBatchAction; Batch.PropertyChanged -= OnBatchPropertyChanged; _operation?.Cancel(); }
}
