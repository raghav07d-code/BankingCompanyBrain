using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanApprovalBenchmark;

public class LoanDbContext : DbContext
{
    public DbSet<Applicant> Applicants { get; set; } = new();
    public DbSet<LoanApplication> LoanApplications { get; set; } = new();
    public DbSet<ApprovalDecision> ApprovalDecisions { get; set; } = new();
    public DbSet<AuditEntry> AuditEntries { get; set; } = new();
    public DbSet<Notification> Notifications { get; set; } = new();
}

public class ApplicantConfiguration : IEntityTypeConfiguration<Applicant>
{
    public void Configure(EntityTypeBuilder<Applicant> builder)
    {
        builder.ToTable("Applicants");
    }
}

public class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
{
    public void Configure(EntityTypeBuilder<LoanApplication> builder)
    {
        builder.ToTable("LoanApplications");
    }
}

public class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.ToTable("ApprovalDecisions");
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditHistory");
    }
}

// Notification is deliberately left unconfigured - no ToTable call anywhere.
