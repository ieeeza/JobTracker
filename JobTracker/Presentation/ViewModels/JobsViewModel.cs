using System.Collections.ObjectModel;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;

namespace JobTracker.Presentation.ViewModels;

public sealed class JobsViewModel : ObservableModel, IDisposable
{
    private readonly JobWorkspace _workspace;
    private readonly TrackingService _tracking;
    private readonly DesktopServices _desktop;
    private readonly UiOperationState _operations;
    private string _search = "", _company = "", _technology = "", _source = "", _role = "";
    private string _sort = "Mais recentes";
    private bool _cards, _trash;
    private DateTime? _from, _to;
    private StatusOption? _statusFilter;
    private JobInfo? _selectedJob;

    public JobsViewModel(JobWorkspace workspace, TrackingService tracking, DesktopServices desktop, UiOperationState operations)
    {
        _workspace = workspace; _tracking = tracking; _desktop = desktop; _operations = operations;
        workspace.Reloaded += OnReloaded;
        OpenJobCommand = operations.Command(nameof(OpenJobCommand), _ => { OpenRequested?.Invoke(RequireSelected()); return Task.CompletedTask; });
        ClearFiltersCommand = operations.Command(nameof(ClearFiltersCommand), _ => { ClearFilters(); return Task.CompletedTask; });
        RecoverCommand = operations.Command(nameof(RecoverCommand), RecoverAsync);
        ArchiveFilteredCommand = operations.Command(nameof(ArchiveFilteredCommand), ArchiveFilteredAsync);
        RefreshCommand = operations.Command(nameof(RefreshCommand), workspace.ReloadAsync);
    }

    public event Action<JobInfo>? OpenRequested;
    public event EventHandler? Filtered;
    public AsyncUiCommand OpenJobCommand { get; }
    public AsyncUiCommand ClearFiltersCommand { get; }
    public AsyncUiCommand RecoverCommand { get; }
    public AsyncUiCommand ArchiveFilteredCommand { get; }
    public AsyncUiCommand RefreshCommand { get; }
    public ObservableCollection<JobInfo> Jobs { get; } = [];
    public JobInfo? SelectedJob { get => _selectedJob; set => Set(ref _selectedJob, value); }
    public bool IsTrash { get => _trash; set { if (Set(ref _trash, value)) Filter(); } }
    public string ResultsLabel => Jobs.Count == 0 ? "Nenhuma vaga encontrada. Cadastre uma oportunidade ou ajuste os filtros." : $"{Jobs.Count} vaga(s) encontrada(s)";
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

    public JobInfo RequireSelected() => SelectedJob ?? throw new InvalidOperationException("Selecione uma vaga para ver os detalhes.");
    private void OnReloaded(object? sender, EventArgs e) => Filter();
    private void ClearFilters()
    {
        Search = CompanyFilter = TechnologyFilter = SourceFilter = RoleFilter = "";
        From = To = null; StatusFilter = null;
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        await _tracking.DeleteAsync(RequireSelected(), false, ct);
        await _workspace.ReloadAsync(ct);
    }

    private async Task ArchiveFilteredAsync(CancellationToken ct)
    {
        var targets = Jobs.Where(j => !j.IsDeleted && j.Status != ApplicationStatus.Archived).ToArray();
        if (targets.Length == 0) throw new InvalidOperationException("Não há vagas para arquivar neste filtro.");
        if (!_desktop.Confirm("Arquivar resultado do filtro", $"Arquivar {targets.Length} vagas exibidas? O histórico permanece disponível. A operação pode ser interrompida entre registros.")) return;
        int completed = 0;
        try
        {
            foreach (var target in targets) { await _tracking.StatusAsync(target, ApplicationStatus.Archived, null, ct); completed++; }
        }
        finally
        {
            _operations.Message = $"{completed} de {targets.Length} vagas arquivadas.";
            await _workspace.ReloadAsync(CancellationToken.None);
        }
    }
    private void Filter()
    {
        static bool Contains(string text, string query) => text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        var query = _workspace.All.Where(j => j.IsDeleted == IsTrash &&
            (string.IsNullOrWhiteSpace(Search) || Contains(string.Join(" ", j.Content.Company, j.Content.Title, j.Content.Description, j.Content.Technologies), Search)) &&
            Contains(j.Content.Company, CompanyFilter) && Contains(j.Content.Title, RoleFilter) && Contains(j.Content.Technologies, TechnologyFilter) && Contains(j.Source, SourceFilter) &&
            (StatusFilter is null || j.Status == StatusFilter.Value) && (From is null || j.CreatedAt.ToLocalTime().Date >= From.Value.Date) && (To is null || j.CreatedAt.ToLocalTime().Date <= To.Value.Date));
        query = Sort switch { "Empresa" => query.OrderBy(j => j.Content.Company), "Cargo" => query.OrderBy(j => j.Content.Title),
            "Mais antigas" => query.OrderBy(j => j.CreatedAt), "Candidatura" => query.OrderByDescending(j => j.AppliedAt), _ => query.OrderByDescending(j => j.CreatedAt) };
        Guid? selected = SelectedJob?.Id;
        Jobs.Clear(); foreach (var job in query) Jobs.Add(job);
        Set(ref _selectedJob, Jobs.FirstOrDefault(j => j.Id == selected), nameof(SelectedJob));
        Raise(nameof(ResultsLabel));
        Filtered?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => _workspace.Reloaded -= OnReloaded;
}
