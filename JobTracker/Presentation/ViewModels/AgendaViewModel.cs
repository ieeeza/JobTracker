using System.Collections.ObjectModel;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;

namespace JobTracker.Presentation.ViewModels;

public sealed class AgendaViewModel : ObservableModel, IDisposable
{
    private readonly JobWorkspace _workspace;
    private DateTime? _agendaFrom = DateTime.Today, _agendaTo;
    private bool _includeCompleted;
    private AppointmentInfo? _selectedAppointment;

    public AgendaViewModel(JobWorkspace workspace, TrackingService tracking, UiOperationState operations)
    {
        _workspace = workspace;
        workspace.Reloaded += OnReloaded;
        OpenAppointmentCommand = operations.Command(nameof(OpenAppointmentCommand), _ =>
        {
            OpenRequested?.Invoke(workspace.Find(RequireAppointment().JobId));
            return Task.CompletedTask;
        });
        CompleteAppointmentCommand = operations.Command(nameof(CompleteAppointmentCommand), async ct =>
        {
            var appointment = RequireAppointment();
            await tracking.CompleteAsync(workspace.Find(appointment.JobId), appointment.Id, ct);
            await workspace.ReloadAsync(ct);
        });
    }

    public event Action<JobInfo>? OpenRequested;
    public AsyncUiCommand OpenAppointmentCommand { get; }
    public AsyncUiCommand CompleteAppointmentCommand { get; }
    public ObservableCollection<AppointmentInfo> Agenda { get; } = [];
    public string AgendaLabel => Agenda.Count == 0 ? "Nenhum compromisso neste período." : $"{Agenda.Count} compromisso(s)";
    public DateTime? AgendaFrom { get => _agendaFrom; set { if (Set(ref _agendaFrom, value)) FilterAgenda(); } }
    public DateTime? AgendaTo { get => _agendaTo; set { if (Set(ref _agendaTo, value)) FilterAgenda(); } }
    public bool IncludeCompleted { get => _includeCompleted; set { if (Set(ref _includeCompleted, value)) FilterAgenda(); } }
    public AppointmentInfo? SelectedAppointment { get => _selectedAppointment; set => Set(ref _selectedAppointment, value); }

    private AppointmentInfo RequireAppointment() => SelectedAppointment ?? throw new InvalidOperationException("Selecione um compromisso.");
    private void OnReloaded(object? sender, EventArgs e) => FilterAgenda();
    private void FilterAgenda()
    {
        Agenda.Clear();
        foreach (var item in _workspace.All.Where(j => !j.IsDeleted).SelectMany(j => j.Appointments).Where(a =>
            (IncludeCompleted || !a.Completed) && (AgendaFrom is null || a.LocalDateTime.Date >= AgendaFrom.Value.Date) &&
            (AgendaTo is null || a.LocalDateTime.Date <= AgendaTo.Value.Date)).OrderBy(a => a.LocalDateTime)) Agenda.Add(item);
        Raise(nameof(AgendaLabel));
    }
    public void Dispose() => _workspace.Reloaded -= OnReloaded;
}
