using System.Collections.ObjectModel;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;

using JobTracker.Modules.Dashboard.Application;

namespace JobTracker.Presentation.ViewModels;


public sealed class BoardViewModel : ObservableModel, IDisposable
{
    private readonly JobsViewModel _jobs;
    private ApplicationStatus _targetStatus = ApplicationStatus.Applied;

    public BoardViewModel(JobsViewModel jobs, TrackingService tracking, DesktopServices desktop, JobWorkspace workspace, UiOperationState operations)
    {
        _jobs = jobs;
        jobs.Filtered += OnFiltered;
        OpenJobCommand = jobs.OpenJobCommand;
        ChangeStatusCommand = operations.Command(nameof(ChangeStatusCommand), async ct =>
        {
            var job = jobs.RequireSelected();
            if (desktop.Confirm("Alterar status", $"{job.Content.Title}\n\nAlterar para {StatusCatalog.Label(TargetStatus)}? A alteração será registrada no histórico."))
            {
                await tracking.StatusAsync(job, TargetStatus, null, ct);
                await workspace.ReloadAsync(ct);
            }
        });
    }

    public AsyncUiCommand OpenJobCommand { get; }
    public AsyncUiCommand ChangeStatusCommand { get; }
    public IReadOnlyList<StatusOption> Statuses => StatusCatalog.All;
    public ApplicationStatus TargetStatus { get => _targetStatus; set => Set(ref _targetStatus, value); }
    public JobInfo? SelectedJob
    {
        get => _jobs.SelectedJob;
        set { if (value is not null) { _jobs.SelectedJob = value; Raise(); } }
    }
    public ObservableCollection<BoardColumn> Board { get; } = [];
    private void OnFiltered(object? sender, EventArgs e)
    {
        Board.Clear();
        foreach (var status in Statuses) Board.Add(new(status.Label, _jobs.Jobs.Where(j => j.Status == status.Value).ToArray()));
        Raise(nameof(SelectedJob));
    }
    public void Dispose() => _jobs.Filtered -= OnFiltered;
}
