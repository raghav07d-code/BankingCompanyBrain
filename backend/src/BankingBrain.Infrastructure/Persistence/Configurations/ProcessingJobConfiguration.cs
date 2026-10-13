using BankingBrain.Domain.Entities.ProcessingJob;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.ToTable("ProcessingJobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany().HasForeignKey(x => x.DocumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.Status, x.AvailableAtUtc });
        builder.HasIndex(x => x.DocumentVersionId)
            .IsUnique()
            .HasFilter("\"Status\" IN (1, 2)");
        builder.HasIndex(x => x.LeaseUntilUtc);
    }
}