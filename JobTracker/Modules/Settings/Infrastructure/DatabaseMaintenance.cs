using System.IO;
using JobTracker.Modules.Settings.Application;
using JobTracker.Modules.JobAnalysis.Infraestructure.Persistence;
using JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Modules.Settings.Infrastructure;

public sealed class DatabaseMaintenance(LocalDatabase database) : IDatabaseMaintenance
{
    public string DatabasePath => database.FilePath;
    public Task BackupAsync(string destination, CancellationToken ct) => Task.Run(() =>
    {
        ValidateDifferent(destination);
        if (File.Exists(destination)) throw new IOException("Escolha um novo nome para o backup; arquivos existentes não são sobrescritos.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Copy(database.FilePath, temporary, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, ct);
    public Task<string> RestoreAsync(string source, CancellationToken ct) => Task.Run(async () =>
    {
        ValidateDifferent(source);
        string folder = Path.Combine(Path.GetDirectoryName(database.FilePath) ?? throw new IOException("Pasta inválida."), "Backups");
        Directory.CreateDirectory(folder);
        string staged = Path.Combine(folder, $"restore-{Guid.NewGuid():N}.db");
        string safety = Path.Combine(folder, $"antes-restauracao-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        try
        {
            Copy(source, staged, ct);
            using (var candidate = Open(staged, SqliteOpenMode.ReadOnly))
            {
                using var check = candidate.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidDataException("O backup não passou na verificação de integridade.");
                check.CommandText = "PRAGMA foreign_key_check;";
                using (var foreignKeys = check.ExecuteReader())
                    if (foreignKeys.Read()) throw new InvalidDataException("O backup contém relacionamentos inválidos.");
                check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Jobs','__EFMigrationsHistory');";
                if (Convert.ToInt64(check.ExecuteScalar()) != 2) throw new InvalidDataException("Este arquivo não é um banco JobTracker compatível.");
            }
            var options = new DbContextOptionsBuilder<JobTrackerDbContext>();
            new LocalDatabase(staged).Configure(options);
            await using (var db = new JobTrackerDbContext(options.Options))
            {
                string[] known = db.Database.GetMigrations().ToArray();
                if ((await db.Database.GetAppliedMigrationsAsync(ct)).Except(known).Any())
                    throw new InvalidDataException("O backup foi criado por uma versão mais recente do JobTracker.");
                await db.Database.MigrateAsync(ct);
                await db.Jobs.Include(j => j.Events).Include(j => j.Tags).Include(j => j.Appointments).AsSplitQuery().Take(1).ToListAsync(ct);
                await db.Set<JobTracker.Modules.Settings.Domain.Preference>().Take(1).ToListAsync(ct);
            }
            Copy(database.FilePath, safety, ct);
            ct.ThrowIfCancellationRequested();
            // SQLite's online backup replaces the destination in a transaction. Do not cancel halfway.
            Copy(staged, database.FilePath, CancellationToken.None);
            return safety;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(staged)) File.Delete(staged);
        }
    }, ct);
    private void ValidateDifferent(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path).Equals(database.FilePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Selecione um arquivo diferente do banco ativo.");
    }
    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 15 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
    private static void Copy(string source, string destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var from = Open(source, SqliteOpenMode.ReadOnly);
        using var to = Open(destination, SqliteOpenMode.ReadWriteCreate);
        from.BackupDatabase(to);
    }
}
