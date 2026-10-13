using BankingBrain.Domain.Entities.Semantics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class SemanticEmbeddingConfiguration : IEntityTypeConfiguration<SemanticEmbedding>
{
    public void Configure(EntityTypeBuilder<SemanticEmbedding> builder)
    {
        builder.ToTable("SemanticEmbeddings");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.SourceType).HasMaxLength(100).IsRequired();
        builder.Property(item => item.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Content).HasColumnType("text").IsRequired();
        builder.Property(item => item.VectorJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.MetadataJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.ProviderIdentity).HasMaxLength(200).IsRequired();
        builder.Property(item => item.ProviderVersion).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Model).HasMaxLength(200).IsRequired();
        builder.HasIndex(item => new { item.WorkspaceId, item.SourceType, item.SourceId, item.ProviderIdentity, item.ProviderVersion, item.Model }).IsUnique();
        builder.HasIndex(item => new { item.WorkspaceId, item.DocumentVersionId });
        builder.HasIndex(item => new { item.WorkspaceId, item.SemanticEntityId });
        builder.HasIndex(item => new { item.WorkspaceId, item.SemanticClaimId });
    }
}