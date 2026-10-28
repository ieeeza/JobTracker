using JobTracker.Modules.JobAnalysis.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace JobTracker.Modules.JobAnalysis.Infraestructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Jobs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ExternalJobId)
            .HasMaxLength(500);

        builder.Property(x => x.OriginalUrl)
            .IsRequired();

        builder.Property(x => x.NormalizedUrl)
            .IsRequired();

        builder.Property(x => x.Company)
            .HasMaxLength(200);

        builder.Property(x => x.Title)
            .HasMaxLength(300);

        builder.Property(x => x.Description);
        builder.Property(x => x.OcrCapture).HasConversion(
            capture => SerializeCapture(capture),
            json => DeserializeCapture(json));

        // SQLite não preserva DateTime.Kind; as datas de candidatura são armazenadas em UTC.
        builder.Property(x => x.AppliedAt).HasConversion(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.HasIndex(x => x.NormalizedUrl).IsUnique();
        builder.Property(x => x.BatchImportKey).HasMaxLength(64);
        builder.HasIndex(x => x.BatchImportKey).IsUnique();

        builder.HasIndex(x => x.AppliedAt);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.HasIndex(x => x.CanonicalUrl).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Appointments).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Tags).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new
        {
            x.Source,
            x.ExternalJobId
        }).IsUnique();
    }

    private static string? SerializeCapture(OcrCapture? capture) =>
        capture is null ? null : JsonSerializer.Serialize(capture);

    private static OcrCapture? DeserializeCapture(string? json)
    {
        if (json is null) return null;
        var capture = JsonSerializer.Deserialize<OcrCapture>(json)
            ?? throw new JsonException("Captura OCR inválida no banco.");
        capture.Validate();
        return capture;
    }
}
