using System.IO;
using JobTracker.Modules.JobAnalysis.Application.Exceptions;
using JobTracker.Modules.JobAnalysis.Infraestructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;

public sealed class PersistenceInitializer(
    LocalDatabase database,
    IDbContextFactory<JobTrackerDbContext> contextFactory,
    JobTracker.Modules.Settings.Application.IDatabaseMaintenance maintenance)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            database.EnsureDirectoryExists();
            await using JobTrackerDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (File.Exists(database.FilePath) && (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                string folder = Path.Combine(Path.GetDirectoryName(database.FilePath) ?? throw new IOException("Pasta inválida."), "Backups");
                Directory.CreateDirectory(folder);
                await maintenance.BackupAsync(Path.Combine(folder, $"antes-migracao-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"), cancellationToken);
            }
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            throw new JobPersistenceException(
                "O banco contém candidaturas duplicadas e não pôde ser atualizado. Preserve um backup e revise os duplicados antes de tentar novamente.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            throw new JobPersistenceException(
                "Não foi possível inicializar o banco local. Verifique as permissões da pasta de dados e se o banco está disponível.", ex);
        }
    }
}
