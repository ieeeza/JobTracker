using JobTracker.Modules.JobAnalysis.Domain;
using Microsoft.EntityFrameworkCore;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Modules.Settings.Domain;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;

public class JobTrackerDbContext(DbContextOptions<JobTrackerDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(JobTrackerDbContext).Assembly
        );
        modelBuilder.Entity<JobTag>().HasKey(t => new { t.JobId, t.Name });
        modelBuilder.Entity<JobEvent>().Property(e => e.OccurredAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        modelBuilder.Entity<Appointment>().HasIndex(a => a.LocalDateTime);
        modelBuilder.Entity<Preference>().HasKey(p => p.Key);
    }
}
