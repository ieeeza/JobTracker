namespace JobTracker.Modules.Settings.Application;

public interface IPreferences
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, CancellationToken ct);
    Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct);
}
public interface IDatabaseMaintenance
{
    string DatabasePath { get; }
    Task BackupAsync(string destination, CancellationToken ct);
    Task<string> RestoreAsync(string source, CancellationToken ct);
}
