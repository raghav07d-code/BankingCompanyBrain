using BankingBrain.Domain.Entities.Refresh;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeSourceRefreshRunConfiguration
    : IEntityTypeConfiguration<KnowledgeSourceRefreshRun>
{
    public void Configure(EntityTypeBuilder<KnowledgeSourceRefreshRun> builder)
    {
        builder.ToTable("KnowledgeSourceRefreshRuns");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkspaceId)
            .IsRequired();

        builder.Property(x => x.KnowledgeSourceId)
            .IsRequired();

        builder.Property(x => x.ExtractorIdentity)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Revision)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.QueuedOnUtc)
            .IsRequired();

        builder.Property(x => x.ErrorCategory)
            .HasMaxLength(200);

        builder.Property(x => x.Error)
            .HasMaxLength(4000);

        // The hot query path: "give me the latest run for this (workspace, knowledge
        // source, extractor) tuple" (status polling) and "supersede every still-active run
        // for this tuple" (ordering guarantee on a new snapshot).
        builder.HasIndex(x => new { x.WorkspaceId, x.KnowledgeSourceId, x.ExtractorIdentity, x.QueuedOnUtc });
    }
}
