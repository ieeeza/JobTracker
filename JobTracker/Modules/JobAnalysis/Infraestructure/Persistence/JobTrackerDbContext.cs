using JobTracker.Modules.JobAnalysis.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;

public class JobTrackerDbContext : DbContext
{
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(
            "Data Source=jobtracker.db"
        );
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(JobTrackerDbContext).Assembly
        );
    }
}