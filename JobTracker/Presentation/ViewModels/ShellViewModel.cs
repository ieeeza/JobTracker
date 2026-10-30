using System.ComponentModel;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;

namespace JobTracker.Presentation.ViewModels;

public sealed class ShellViewModel : ObservableModel, IDisposable
{
    private readonly DesktopServices _desktop;
    private readonly JobWorkspace _workspace;
    private PageDestination _page = PageDestination.Dashboard;

    public ShellViewModel(DesktopServices desktop, JobWorkspace workspace, UiOperationState operations,
        DashboardViewModel dashboard, JobsViewModel jobs, BoardViewModel board, AgendaViewModel agenda,
        SettingsViewModel settings, JobEditorViewModel editor)
    {
        _desktop = desktop; _workspace = workspace; Operations = operations;
        Dashboard = dashboard; Jobs = jobs; Board = board; Agenda = agenda; Settings = settings; Editor = editor;
        NavigateCommand = new UiCommand<PageDestination>(NavigateTo, () => Operations.IsIdle);
        CancelCommand = new UiCommand(operations.Cancel, () => Operations.IsBusy);
        SaveCommand = new UiCommand(() => Editor.SaveCommand.Execute(null),
            () => IsEditorPage && Editor.SaveCommand.CanExecute(null));
        RefreshCommand = operations.Command(nameof(RefreshCommand), ct => IsEditorPage ? Editor.RefreshAsync(ct) : workspace.ReloadAsync(ct));
        operations.PropertyChanged += OnOperationChanged;
        jobs.OpenRequested += OpenJob;
        dashboard.OpenRequested += OpenJob;
        agenda.OpenRequested += OpenJob;
        editor.Batch.OpenJobRequested += OpenJob;
        editor.SaveCommand.CanExecuteChanged += OnEditorSaveCanExecuteChanged;
        editor.Opened += OnEditorOpened;
        editor.Created += OnEditorCreated;
        editor.Deleted += OnEditorDeleted;
        settings.DatabaseRestored += OnDatabaseRestoredAsync;
    }

    public UiOperationState Operations { get; }
    public DashboardViewModel Dashboard { get; }
    public JobsViewModel Jobs { get; }
    public BoardViewModel Board { get; }
    public AgendaViewModel Agenda { get; }
    public SettingsViewModel Settings { get; }
    public JobEditorViewModel Editor { get; }
    public UiCommand<PageDestination> NavigateCommand { get; }
    public UiCommand CancelCommand { get; }
    public UiCommand SaveCommand { get; }
    public AsyncUiCommand RefreshCommand { get; }
    public PageDestination Page
    {
        get => _page;
        private set
        {
            Set(ref _page, value);
            Raise(nameof(PageTitle)); Raise(nameof(PageDescription)); Raise(nameof(CurrentViewModel)); Raise(nameof(IsEditorPage));
            SaveCommand.Refresh();
        }
    }
    public bool IsEditorPage => Page is PageDestination.NewJob or PageDestination.Details;
    public ObservableModel CurrentViewModel => Page switch
    {
        PageDestination.Dashboard => Dashboard,
        PageDestination.Jobs or PageDestination.Trash => Jobs,
        PageDestination.Board => Board,
        PageDestination.Agenda => Agenda,
        PageDestination.Settings => Settings,
        _ => Editor
    };
    public string PageTitle => Page switch
    {
        PageDestination.Dashboard => "Dashboard", PageDestination.Jobs => "Vagas",
        PageDestination.NewJob => "Nova vaga", PageDestination.Details => "Detalhes",
        PageDestination.Board => "Candidaturas", PageDestination.Agenda => "Agenda",
        PageDestination.Settings => "Configurações", PageDestination.Trash => "Lixeira",
        _ => throw new InvalidOperationException("Destino de navegação inválido.")
    };
    public string PageDescription => Page switch
    {
        PageDestination.Dashboard => "Uma visão real da sua busca pelo próximo passo.",
        PageDestination.Jobs => "Pesquise, filtre e organize oportunidades.",
        PageDestination.NewJob => "Escolha como preencher, revise e salve. O próximo cadastro fica pronto automaticamente.",
        PageDestination.Details => "Edite os dados, acompanhe o processo e planeje sua próxima ação.",
        PageDestination.Board => "Seu processo, etapa por etapa. Selecione uma vaga para alterar o status.",
        PageDestination.Agenda => "Entrevistas, prazos e lembretes no horário local.",
        PageDestination.Trash => "Os registros permanecem recuperáveis e não entram nas métricas.",
        _ => "Aparência, captura e controle dos seus dados locais."
    };

    public Task InitializeAsync() => Operations.RunAsync(nameof(InitializeAsync), async ct =>
    {
        await Settings.LoadAsync(ct);
        await Editor.Batch.InitializeAsync(ct);
        await _workspace.ReloadAsync(ct);
    });

    private void NavigateTo(PageDestination destination)
    {
        if (!Enum.IsDefined(destination) || Operations.IsBusy) return;
        if (IsEditorPage && (destination != Page || destination == PageDestination.NewJob) && !Editor.ConfirmDiscard()) return;
        if (destination == PageDestination.NewJob) Editor.NewEditor();
        Jobs.IsTrash = destination == PageDestination.Trash;
        Page = destination;
        if (destination == PageDestination.NewJob) Editor.RequestFocus();
    }

    private void OpenJob(JobInfo job)
    {
        if (IsEditorPage && !Editor.ConfirmDiscard()) return;
        Editor.Open(job);
    }
    private void OnEditorSaveCanExecuteChanged(object? sender, EventArgs e) => SaveCommand.Refresh();
    private void OnEditorOpened(object? sender, EventArgs e) => Page = PageDestination.Details;
    private void OnEditorCreated(object? sender, EventArgs e) => Page = PageDestination.NewJob;
    private void OnEditorDeleted(object? sender, EventArgs e) { Jobs.IsTrash = false; Page = PageDestination.Jobs; }
    private async Task OnDatabaseRestoredAsync(CancellationToken ct)
    {
        Editor.ResetAfterRestore();
        await Editor.Batch.InitializeAsync(ct);
    }
    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UiOperationState.IsBusy)) return;
        NavigateCommand.Refresh(); CancelCommand.Refresh(); SaveCommand.Refresh();
    }

    public bool NeedsCloseConfirmation => Operations.IsBusy || IsEditorPage && Editor.HasUnsavedChanges;
    public bool ConfirmClose() => _desktop.Confirm("Fechar JobTracker", Operations.IsBusy
        ? "Há uma operação em andamento. Deseja cancelá-la e fechar após liberar os recursos?"
        : "As alterações não salvas no formulário serão descartadas. Deseja fechar?");
    public Task CancelAndWaitAsync() => Operations.CancelAndWaitAsync();

    public void Dispose()
    {
        Operations.PropertyChanged -= OnOperationChanged;
        Jobs.OpenRequested -= OpenJob; Dashboard.OpenRequested -= OpenJob; Agenda.OpenRequested -= OpenJob;
        Editor.Batch.OpenJobRequested -= OpenJob;
        Editor.SaveCommand.CanExecuteChanged -= OnEditorSaveCanExecuteChanged;
        Editor.Opened -= OnEditorOpened; Editor.Created -= OnEditorCreated; Editor.Deleted -= OnEditorDeleted;
        Settings.DatabaseRestored -= OnDatabaseRestoredAsync;
    }
}
