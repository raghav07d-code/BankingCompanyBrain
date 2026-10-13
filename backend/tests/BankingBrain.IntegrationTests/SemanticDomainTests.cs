using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Domain.Enums;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class SemanticDomainTests
{
    [Fact]
    public void Entity_normalization_merges_punctuation_and_common_legal_suffixes()
    {
        Assert.Equal("ACME", SemanticEntity.Normalize("Acme, Inc."));
        Assert.Equal(SemanticEntity.Normalize("Acme Inc"), SemanticEntity.Normalize("ACME"));
    }

    [Fact]
    public void Semantic_job_retries_then_becomes_terminal()
    {
        var job = SemanticProcessingJob.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "rules", "1");
        job.Claim(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(1));
        Assert.True(job.ScheduleRetry("temporary", DateTime.UtcNow.AddMinutes(1), 2));
        job.Claim(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(1));
        Assert.False(job.ScheduleRetry("terminal", DateTime.UtcNow.AddMinutes(1), 2));
        Assert.Equal(ProcessingJobStatus.Failed, job.Status);
    }

    [Fact]
    public void Claims_with_different_evidence_remain_distinct()
    {
        var workspace = Guid.NewGuid();
        var version = Guid.NewGuid();
        var run = Guid.NewGuid();
        var firstEvidence = Guid.NewGuid();
        var secondEvidence = Guid.NewGuid();
        var first = SemanticClaim.Create(workspace, version, run, firstEvidence, null, null, "Acme", "owns", "Loan", .7m);
        var second = SemanticClaim.Create(workspace, version, run, secondEvidence, null, null, "Acme", "owns", "Loan", .7m);
        Assert.NotEqual(first.IdentityHash, second.IdentityHash);
    }
}