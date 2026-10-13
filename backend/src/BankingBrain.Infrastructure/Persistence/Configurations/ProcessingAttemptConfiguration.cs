using BankingBrain.Domain.Entities.ProcessingAttempt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingBrain.Infrastructure.Persistence.Configurations;

public sealed class ProcessingAttemptConfiguration
    : IEntityTypeConfiguration<ProcessingAttempt>
{
    public void Configure(EntityTypeBuilder<ProcessingAttempt> builder)
    {
        builder.ToTable("ProcessingAttempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProcessorIdentity).HasMaxLength(200);
        builder.Property(x => x.ProcessorVersion).HasMaxLength(200);
        builder.Property(x => x.Error).HasMaxLength(4000);
        builder.HasOne<BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion>()
            .WithMany()
            .HasForeignKey(x => x.DocumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.DocumentVersionId, x.AttemptNumber }).IsUnique();
    }
}