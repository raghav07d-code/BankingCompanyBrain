using BankingBrain.Domain.Entities.ProvenanceAnchor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class ProvenanceAnchorConfiguration
    : IEntityTypeConfiguration<ProvenanceAnchor>
{
    public void Configure(EntityTypeBuilder<ProvenanceAnchor> builder)
    {
        builder.ToTable("ProvenanceAnchors");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LocationReference).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SheetName).HasMaxLength(200);
        builder.Property(x => x.SectionReference).HasMaxLength(500);
        builder.Property(x => x.ExtractedValue).HasColumnType("text");
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany()
            .HasForeignKey(x => x.DocumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.DocumentVersionId);
    }
}