using JobTracker.Modules.JobAnalysis.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
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
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(x => x.Description);

        builder.HasIndex(x => new
        {
            x.Source,
            x.ExternalJobId
        });
    }
}