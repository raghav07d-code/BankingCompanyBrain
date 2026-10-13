using BankingBrain.Domain.Entities.DocumentVersion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class DocumentVersionConfiguration
    : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("DocumentVersions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentId)
            .IsRequired();

        builder.Property(x => x.WorkspaceId);
        builder.HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();

        builder.Property(x => x.VersionNumber)
            .IsRequired();

        builder.Property(x => x.ContentHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.MediaType)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.StorageKey)
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(x => x.FileSize)
            .IsRequired();

        builder.Property(x => x.RawContentLocation)
            .HasMaxLength(2048);

        builder.Property(x => x.ExtractedText)
            .HasColumnType("text");

        builder.Property(x => x.ProcessingState)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.CreatedOnUtc)
            .IsRequired();

        builder.Property(x => x.ProcessingError)
            .HasMaxLength(4000);

        builder.Property(x => x.ProcessorVersion)
            .HasMaxLength(200);

        builder.HasOne<BankingBrain.Domain.Entities.Document.Document>()
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.DocumentId, x.VersionNumber })
            .IsUnique();

        builder.HasIndex(x => new { x.DocumentId, x.ContentHash })
            .IsUnique();

        builder.HasIndex(x => x.DocumentId);
    }
}