using JobTracker.Modules.JobApplications.Application;

namespace JobTracker.Presentation.Services;

/// <summary>Publishes the current application DTOs to the persistent screen instances.</summary>
public sealed class JobWorkspace(TrackingService tracking)
{
    public IReadOnlyList<JobInfo> All { get; private set; } = [];
    public event EventHandler? Reloaded;

    public async Task ReloadAsync(CancellationToken ct)
    {
        All = await tracking.ListAsync(ct);
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    public JobInfo Find(Guid id) => All.SingleOrDefault(job => job.Id == id)
        ?? throw new InvalidOperationException("Registro não encontrado. Atualize os dados.");
}
