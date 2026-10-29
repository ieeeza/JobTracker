using System.Collections.ObjectModel;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;

namespace JobTracker.Presentation.ViewModels;

public sealed class DashboardViewModel : ObservableModel, IDisposable
{
    private readonly JobWorkspace _workspace;
    private JobInfo? _selectedJob;
    public DashboardViewModel(JobWorkspace workspace, UiOperationState operations)
    {
        _workspace = workspace;
        workspace.Reloaded += OnReloaded;
        OpenJobCommand = operations.Command(nameof(OpenJobCommand), _ =>
        {
            OpenRequested?.Invoke(SelectedJob ?? throw new InvalidOperationException("Selecione uma vaga para ver os detalhes."));
            return Task.CompletedTask;
        });
    }

    public event Action<JobInfo>? OpenRequested;
    public AsyncUiCommand OpenJobCommand { get; }
    public JobInfo? SelectedJob { get => _selectedJob; set => Set(ref _selectedJob, value); }
    public JobTracker.Modules.Dashboard.Application.DashboardSummary Dashboard { get; private set; } = JobTracker.Modules.Dashboard.Application.DashboardSummary.From([]);
    private void OnReloaded(object? sender, EventArgs e)
    {
        Dashboard = JobTracker.Modules.Dashboard.Application.DashboardSummary.From(_workspace.All);
        SelectedJob = _workspace.All.SingleOrDefault(job => job.Id == SelectedJob?.Id);
        Raise(nameof(Dashboard));
    }
    public void Dispose() => _workspace.Reloaded -= OnReloaded;
}
