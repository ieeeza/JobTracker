using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Modules.JobAnalysis.Infraestructure.Persistence.Repositories
{
    public class JobRepository(JobTrackerDbContext context) : IJobRepository
    {
        private readonly JobTrackerDbContext _context = context;

        public async Task<Job?> GetByExternalIdAsync(
            string source,
            string externalJobId,
            CancellationToken cancellationToken)
        {
            return await _context.Jobs
                .FirstOrDefaultAsync(
                    x => x.Source == source && x.ExternalJobId == externalJobId,
                    cancellationToken);
        }

        public async Task<Job?> GetByNormalizedUrlAsync(
            string normalizedUrl,
            CancellationToken cancellationToken)
        {
            return await _context.Jobs
                .FirstOrDefaultAsync(
                    x => x.NormalizedUrl == normalizedUrl,
                    cancellationToken);
        }

        public async Task AddAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            await _context.Jobs.AddAsync(
                job,
                cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            await _context.SaveChangesAsync(
                cancellationToken);
        }
    }
}
