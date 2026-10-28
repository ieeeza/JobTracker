using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;

internal sealed class AnalysisCacheRow
{
    public string Key { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class AnalysisCacheConfiguration : IEntityTypeConfiguration<AnalysisCacheRow>
{
    public void Configure(EntityTypeBuilder<AnalysisCacheRow> builder)
    {
        builder.ToTable("VisionAnalysisCache");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(64);
    }
}

public sealed class AnalysisCache(IDbContextFactory<JobTrackerDbContext> factory) : IAnalysisCache
{
    public async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<AnalysisCacheRow>().Where(x => x.Key == key).Select(x => x.Payload).SingleOrDefaultAsync(ct);
    }

    public async Task WriteAsync(string key, string payload, CancellationToken ct)
    {
        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit) || payload.Length > 2 * 1024 * 1024)
            throw new ArgumentException("Entrada de cache inválida.");
        await using var db = await factory.CreateDbContextAsync(ct);
        // All inference callers share the client's semaphore; no transaction spans inference.
        var row = await db.Set<AnalysisCacheRow>().FindAsync([key], ct);
        if (row is null) db.Add(new AnalysisCacheRow { Key = key, Payload = payload, CreatedAtUtc = DateTime.UtcNow });
        else { row.Payload = payload; row.CreatedAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
}
