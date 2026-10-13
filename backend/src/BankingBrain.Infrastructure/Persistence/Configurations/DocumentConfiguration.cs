using BankingBrain.Domain.Entities.Document;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration
    : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkspaceId)
            .IsRequired();

        builder.Property(x => x.KnowledgeSourceId)
            .IsRequired();

        builder.Property(x => x.CurrentVersionId);

        builder.Property(x => x.Name)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.ExternalIdentifier)
            .HasMaxLength(500);

        builder.Property(x => x.MimeType)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.CreatedOnUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedOnUtc)
            .IsRequired();

        builder.HasOne<BankingBrain.Domain.Entities.Workspace.Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource>()
            .WithMany()
            .HasForeignKey(x => new { x.KnowledgeSourceId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany()
            .HasForeignKey(x => x.CurrentVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.WorkspaceId,
            x.KnowledgeSourceId,
            x.Name
        })
        .IsUnique();

        builder.HasIndex(x => new
        {
            x.WorkspaceId,
            x.KnowledgeSourceId
        });

        builder.HasIndex(x => x.ExternalIdentifier);
    }
}