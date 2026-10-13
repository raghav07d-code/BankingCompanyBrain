using BankingBrain.Domain.Entities.Semantics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class SemanticGraphNodeConfiguration : IEntityTypeConfiguration<SemanticGraphNode>
{
    public void Configure(EntityTypeBuilder<SemanticGraphNode> builder)
    {
        builder.ToTable("SemanticGraphNodes"); builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.WorkspaceId, item.EntityId }).IsUnique();
    }
}

public sealed class SemanticGraphEdgeConfiguration : IEntityTypeConfiguration<SemanticGraphEdge>
{
    public void Configure(EntityTypeBuilder<SemanticGraphEdge> builder)
    {
        builder.ToTable("SemanticGraphEdges"); builder.HasKey(item => item.Id);
        builder.Property(item => item.Predicate).HasMaxLength(200).IsRequired();
        builder.Property(item => item.IdentityHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.SupportingClaimIdsJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.EvidenceIdsJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.ExtractionConfidence).HasPrecision(5, 4);
        builder.Property(item => item.SourceAuthorityScore).HasPrecision(5, 4);
        builder.HasIndex(item => new { item.WorkspaceId, item.IdentityHash }).IsUnique();
        builder.HasIndex(item => new { item.WorkspaceId, item.SubjectEntityId, item.Predicate });
        builder.HasIndex(item => new { item.WorkspaceId, item.ObjectEntityId, item.Predicate });
        builder.HasIndex(item => new { item.WorkspaceId, item.AsOfUtc, item.ValidFromUtc, item.ValidToUtc });
    }
}