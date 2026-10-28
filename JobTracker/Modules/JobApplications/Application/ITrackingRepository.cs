using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobApplications.Application;

public interface ITrackingRepository
{
    Task<IReadOnlyList<Job>> ReadAllAsync(CancellationToken cancellationToken);
    Task AddAsync(Job job, CancellationToken cancellationToken);
    Task ChangeAsync(Guid id, Guid revision, Action<Job> change, CancellationToken cancellationToken);
    Task ImportAsync(IReadOnlyList<Job> jobs, CancellationToken cancellationToken);
}
