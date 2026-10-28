using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobAnalysis.Application.Exceptions;
using JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;
using JobTracker.Modules.JobApplications.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Modules.JobApplications.Infrastructure;

public sealed class TrackingRepository(IDbContextFactory<JobTrackerDbContext> factory) : ITrackingRepository
{
    public Task<IReadOnlyList<Job>> ReadAllAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<Job>>(async () =>
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Jobs.AsNoTracking().Include(j => j.Tags).Include(j => j.Events)
            .Include(j => j.Appointments).AsSplitQuery().OrderByDescending(j => j.CreatedAt).ToArrayAsync(cancellationToken);
    }, cancellationToken);
    public Task AddAsync(Job job, CancellationToken cancellationToken) => ImportAsync([job], cancellationToken);
    public Task ImportAsync(IReadOnlyList<Job> jobs, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Jobs.AddRange(jobs);
        await SaveAsync(db, cancellationToken);
    }, cancellationToken);
    public Task ChangeAsync(Guid id, Guid revision, Action<Job> change, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        Job job = await db.Jobs.Include(j => j.Tags).Include(j => j.Events).Include(j => j.Appointments)
            .AsSplitQuery().SingleOrDefaultAsync(j => j.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("A vaga não foi encontrada. Atualize a listagem.");
        if (job.Revision != revision) throw new InvalidOperationException("A vaga foi alterada. Atualize a listagem antes de salvar novamente.");
        var eventIds = job.Events.Select(e => e.Id).ToHashSet();
        var appointmentIds = job.Appointments.Select(a => a.Id).ToHashSet();
        change(job);
        db.AddRange(job.Events.Where(e => !eventIds.Contains(e.Id)));
        db.AddRange(job.Appointments.Where(a => !appointmentIds.Contains(a.Id)));
        await SaveAsync(db, cancellationToken);
    }, cancellationToken);
    private static async Task SaveAsync(JobTrackerDbContext db, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException ex)
        { throw new JobPersistenceException("A vaga foi alterada por outra operação. Atualize a listagem.", ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        { throw new DuplicateJobException(ex); }
    }
}
