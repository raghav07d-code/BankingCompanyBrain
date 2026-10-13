using BankingBrain.Domain.Entities.Semantics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class SemanticEntityConfiguration : IEntityTypeConfiguration<SemanticEntity>
{
    public void Configure(EntityTypeBuilder<SemanticEntity> builder)
    {
        builder.ToTable("SemanticEntities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        // 2000 chars comfortably covers real fully-qualified C# names/signatures
        // (Roslyn's CSharpErrorMessageFormat spells out every parameter type), while
        // staying well under Postgres' per-column btree index size limit (these
        // columns back unique indexes below).
        builder.Property(x => x.CanonicalName).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Aliases).HasColumnType("text");
            builder.Property(x => x.ExternalIdentifier).HasMaxLength(2000);
        builder.HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.EntityType, x.NormalizedName }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.EntityType, x.ExternalIdentifier }).IsUnique();
        // Backs FindCodeEntityAsync's tier-1 exact-CanonicalName lookup (workspace-scoped);
        // not unique, since two entities can share a CanonicalName (that tier already
        // treats >1 match as ambiguous and falls through rather than assuming uniqueness).
        builder.HasIndex(x => new { x.WorkspaceId, x.CanonicalName });
    }
}

public sealed class SemanticEntityTypeConfiguration : IEntityTypeConfiguration<SemanticEntityType>
{
    public void Configure(EntityTypeBuilder<SemanticEntityType> builder)
    {
        builder.ToTable("SemanticEntityTypes"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired(); builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => new { x.WorkspaceId, x.Code }).IsUnique();
    }
}

public sealed class SemanticPredicateConfiguration : IEntityTypeConfiguration<SemanticPredicate>
{
    public void Configure(EntityTypeBuilder<SemanticPredicate> builder)
    {
        builder.ToTable("SemanticPredicates"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired(); builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); builder.Property(x => x.AllowedSubjectTypes).HasColumnType("text");
            builder.Property(x => x.Code).HasMaxLength(100).IsRequired(); builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); builder.Property(x => x.AllowedSubjectTypes).HasColumnType("text"); builder.Property(x => x.AllowedObjectTypes).HasColumnType("text");
        builder.HasIndex(x => new { x.WorkspaceId, x.Code }).IsUnique();
    }
}

public sealed class SemanticExtractionRunConfiguration : IEntityTypeConfiguration<SemanticExtractionRun>
{
    public void Configure(EntityTypeBuilder<SemanticExtractionRun> builder)
    {
        builder.ToTable("SemanticExtractionRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExtractorIdentity).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExtractorVersion).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.ErrorCategory).HasMaxLength(100);
        builder.Property(x => x.Error).HasMaxLength(4000);
        builder.HasIndex(x => new { x.WorkspaceId, x.DocumentVersionId, x.ExtractorIdentity, x.ExtractorVersion });
        builder.HasIndex(x => new { x.WorkspaceId, x.DocumentVersionId, x.StartedOnUtc });
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany().HasForeignKey(x => new { x.WorkspaceId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SemanticEvidenceConfiguration : IEntityTypeConfiguration<SemanticEvidence>
{
    public void Configure(EntityTypeBuilder<SemanticEvidence> builder)
    {
        builder.ToTable("SemanticEvidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceLocation).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SheetName).HasMaxLength(255);
        builder.Property(x => x.CellReference).HasMaxLength(50);
        builder.Property(x => x.ExtractedText).HasColumnType("text");
        builder.Property(x => x.ExtractedValue).HasColumnType("text");
        builder.HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.DocumentVersionId });
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany().HasForeignKey(x => new { x.WorkspaceId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SemanticExtractionRun>()
            .WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ExtractionRunId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SemanticClaimConfiguration : IEntityTypeConfiguration<SemanticClaim>
{
    public void Configure(EntityTypeBuilder<SemanticClaim> builder)
    {
        builder.ToTable("SemanticClaims");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Predicate).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ObjectValue).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ExtractionConfidence).HasPrecision(5, 4).IsRequired();
        builder.Property(x => x.SourceAuthorityScore).HasPrecision(5, 4);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.IdentityHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ValueKind).HasMaxLength(50).IsRequired(); builder.Property(x => x.CurrencyCode).HasMaxLength(3); builder.Property(x => x.Unit).HasMaxLength(100); builder.Property(x => x.NumericValue).HasPrecision(24, 8);
        builder.HasIndex(x => new { x.WorkspaceId, x.DocumentVersionId, x.IdentityHash }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.SubjectEntityId });
        builder.HasIndex(x => new { x.WorkspaceId, x.ObjectEntityId });
        builder.HasIndex(x => new { x.WorkspaceId, x.SubjectEntityId, x.Predicate, x.AsOfUtc });
        builder.HasIndex(x => new { x.WorkspaceId, x.Predicate, x.ValidFromUtc, x.ValidToUtc });
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany().HasForeignKey(x => new { x.WorkspaceId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SemanticEvidence>()
            .WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EvidenceId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SemanticEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SubjectEntityId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SemanticEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObjectEntityId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SemanticRelationshipConfiguration : IEntityTypeConfiguration<SemanticRelationship>
{
    public void Configure(EntityTypeBuilder<SemanticRelationship> builder)
    {
        builder.ToTable("SemanticRelationships");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RelationType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExtractionConfidence).HasPrecision(5, 4).IsRequired();
        builder.Property(x => x.IdentityHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.WorkspaceId, x.DocumentVersionId, x.IdentityHash }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.SubjectEntityId });
        builder.HasIndex(x => new { x.WorkspaceId, x.ObjectEntityId });
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany().HasForeignKey(x => new { x.WorkspaceId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SemanticEvidence>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EvidenceId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SemanticEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SubjectEntityId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SemanticEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObjectEntityId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
