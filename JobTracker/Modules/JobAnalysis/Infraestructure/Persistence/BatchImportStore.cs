using System.Text.Json;
using System.Text.Json.Serialization;
using JobTracker.Modules.JobAnalysis.Application.BatchImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;

internal sealed class ImportBatchRow
{
    public Guid Id { get; set; }
    public Guid Revision { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public string Folder { get; set; } = "";
    public ImportBatchStatus Status { get; set; }
    public string Payload { get; set; } = "";
}
internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatchRow>
{
    public void Configure(EntityTypeBuilder<ImportBatchRow> builder)
    {
        builder.ToTable("ScreenshotImportBatches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Revision).IsConcurrencyToken();
        builder.HasIndex(b => b.Fingerprint).IsUnique();
        builder.HasIndex(b => b.StartedAtUtc);
        builder.Property(b => b.StartedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    }
}

public sealed class BatchImportStore(IDbContextFactory<JobTrackerDbContext> factory) : IImportBatchStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { RespectNullableAnnotations = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32, IgnoreReadOnlyProperties = true };

    public async Task SaveAsync(ImportBatch batch, CancellationToken ct)
    {
        if (batch.Images.Select(i => i.Id).Distinct().Count() != batch.Images.Count ||
            batch.Groups.Select(g => g.Id).Distinct().Count() != batch.Groups.Count ||
            batch.Images.Any(i => i.GroupId is { } id && batch.Groups.All(g => g.Id != id)))
            throw new InvalidOperationException("Associação de imagens e grupos inconsistente.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var stored = await db.Set<ImportBatchRow>().SingleOrDefaultAsync(b => b.Id == batch.Id, ct);
        if (stored is null)
        { stored = new ImportBatchRow { Id = batch.Id }; db.Add(stored); }
        else if (stored.Revision != batch.Revision)
            throw new InvalidOperationException("O lote foi alterado por outra operação. Reabra o relatório.");
        Guid revision = Guid.NewGuid();
        stored.Revision = revision; stored.Fingerprint = batch.Fingerprint; stored.StartedAtUtc = batch.StartedAtUtc;
        stored.Folder = batch.Folder; stored.Status = batch.Status;
        stored.Payload = JsonSerializer.Serialize(batch, JsonOptions);
        // Once a short checkpoint starts committing, finish it so the in-memory revision
        // cannot disagree with a committed row when cancellation arrives concurrently.
        await db.SaveChangesAsync(CancellationToken.None);
        batch.Revision = revision;
    }

    public async Task<ImportBatch> LoadAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Set<ImportBatchRow>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new InvalidOperationException("Lote não encontrado.");
        var batch = JsonSerializer.Deserialize<ImportBatch>(row.Payload, JsonOptions) ?? throw new JsonException("Histórico do lote inválido.");
        batch.Revision = row.Revision;
        return batch;
    }
    public async Task<IReadOnlyList<BatchHistoryEntry>> HistoryAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<ImportBatchRow>().AsNoTracking().OrderByDescending(b => b.StartedAtUtc)
            .Select(b => new BatchHistoryEntry(b.Id, b.StartedAtUtc, b.Folder, b.Status)).ToArrayAsync(ct);
    }
    public async Task<Guid?> FindAsync(string fingerprint, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<ImportBatchRow>().Where(b => b.Fingerprint == fingerprint).Select(b => (Guid?)b.Id).SingleOrDefaultAsync(ct);
    }
}
