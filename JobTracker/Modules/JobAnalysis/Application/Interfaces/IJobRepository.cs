using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobAnalysis.Application.Interfaces
{
    public interface IJobRepository
    {
        Task<Job?> GetByExternalIdAsync(
        string source,
        string externalJobId,
        CancellationToken cancellationToken);

        Task<Job?> GetByNormalizedUrlAsync(
            string normalizedUrl,
            CancellationToken cancellationToken);

        Task AddAsync(
            Job job,
            CancellationToken cancellationToken);

        Task SaveChangesAsync(
            CancellationToken cancellationToken);
    }
}
