using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace JobTracker.Modules.JobAnalysis.Infraestructure.Persistence;

public sealed class LocalDatabase
{
    public string FilePath { get; }

    public LocalDatabase(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("O caminho do banco deve ser absoluto.", nameof(filePath));

        FilePath = Path.GetFullPath(filePath);
    }

    public static LocalDatabase ForCurrentUser()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new IOException("Não foi possível localizar a pasta de dados do usuário.");

        return new LocalDatabase(Path.Combine(localAppData, "JobTracker", "jobtracker.db"));
    }

    public void EnsureDirectoryExists()
    {
        string directory = Path.GetDirectoryName(FilePath)
            ?? throw new IOException("O caminho do banco não possui um diretório válido.");
        Directory.CreateDirectory(directory);
    }

    public void Configure(DbContextOptionsBuilder options)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString();

        options.UseSqlite(connectionString);
    }
}
