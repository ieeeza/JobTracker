using JobTracker.Modules.JobAnalysis.Infraestructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;

public sealed class JobTrackerDbContextFactory : IDesignTimeDbContextFactory<JobTrackerDbContext>
{
    public JobTrackerDbContext CreateDbContext(string[] args)
    {
        LocalDatabase database = args.Length switch
        {
            0 => LocalDatabase.ForCurrentUser(),
            2 when args[0] == "--database" => new LocalDatabase(args[1]),
            _ => throw new ArgumentException("Use --database <caminho absoluto> para escolher um banco nas ferramentas EF.")
        };

        database.EnsureDirectoryExists();
        DbContextOptionsBuilder<JobTrackerDbContext> options = new();
        database.Configure(options);
        return new JobTrackerDbContext(options.Options);
    }
}
