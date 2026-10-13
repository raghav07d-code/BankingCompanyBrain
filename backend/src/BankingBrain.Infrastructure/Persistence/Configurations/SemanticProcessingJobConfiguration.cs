using BankingBrain.Domain.Entities.Semantics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class SemanticProcessingJobConfiguration : IEntityTypeConfiguration<SemanticProcessingJob>
{
    public void Configure(EntityTypeBuilder<SemanticProcessingJob> builder)
    {
        builder.ToTable("SemanticProcessingJobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExtractorIdentity).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExtractorVersion).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.HasIndex(x => new { x.Status, x.AvailableAtUtc });
        builder.HasIndex(x => x.LeaseUntilUtc);
        builder.HasIndex(x => new { x.WorkspaceId, x.DocumentVersionId, x.ExtractorIdentity, x.ExtractorVersion })
            .IsUnique().HasFilter("\"Status\" IN (1, 2)");
        builder.HasOne<SemanticExtractionRun>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ExtractionRunId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
