using BankingBrain.Domain.Entities.DocumentSection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class DocumentSectionConfiguration
    : IEntityTypeConfiguration<DocumentSection>
{
    public void Configure(EntityTypeBuilder<DocumentSection> builder)
    {
        builder.ToTable("DocumentSections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(500);
        builder.Property(x => x.Text).HasColumnType("text");
        builder.Property(x => x.SourceLocation).HasMaxLength(500);
        builder.Property(x => x.ContentType).HasMaxLength(100);
        builder.Property(x => x.SheetName).HasMaxLength(255);
        builder.Property(x => x.CellReference).HasMaxLength(50);
        builder.Property(x => x.Value).HasColumnType("text");
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany()
            .HasForeignKey(x => x.DocumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.DocumentVersionId, x.Sequence }).IsUnique();
    }
}