using JobTracker.Modules.Settings.Application;
using JobTracker.Modules.Settings.Domain;
using JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Modules.Settings.Infrastructure;

public sealed class SqlitePreferences(IDbContextFactory<JobTrackerDbContext> factory) : IPreferences
{
    public Task<string?> GetAsync(string key, CancellationToken ct) => Task.Run(async () =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<Preference>().Where(p => p.Key == key).Select(p => p.Value).SingleOrDefaultAsync(ct);
    }, ct);
    public Task SetAsync(string key, string value, CancellationToken ct) => SetManyAsync(new Dictionary<string, string> { [key] = value }, ct);
    public Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct) => Task.Run(async () =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        foreach (var (key, value) in values)
        {
            if (key.Length is 0 or > 100 || value.Length > 10000) throw new ArgumentException("Preferência inválida.");
            var setting = await db.Set<Preference>().FindAsync([key], ct);
            if (setting is null) db.Add(new Preference { Key = key, Value = value });
            else setting.Value = value;
        }
        await db.SaveChangesAsync(ct);
    }, ct);
}
