using BankingBrain.Domain.Entities.DocumentVersion;
using BankingBrain.Domain.Entities.ProcessingJob;
using BankingBrain.Domain.Enums;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class ProcessingReliabilityTests
{
    [Fact]
    public void Retry_preserves_job_history_and_persists_next_availability()
    {
        var job = ProcessingJob.Create(Guid.NewGuid());
        var leaseToken = Guid.NewGuid();
        var retryAt = DateTime.UtcNow.AddMinutes(3);

        job.Claim(leaseToken, DateTime.UtcNow.AddMinutes(1));
        var scheduled = job.ScheduleRetry("transient", retryAt, maxAttempts: 3);

        Assert.True(scheduled);
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(retryAt, job.AvailableAtUtc);
        Assert.Null(job.LeaseToken);
        Assert.Null(job.LeaseUntilUtc);
        Assert.Equal("transient", job.LastError);
    }

    [Fact]
    public void Retry_becomes_terminal_after_max_attempts()
    {
        var job = ProcessingJob.Create(Guid.NewGuid());
        job.Claim(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(1));
        job.ScheduleRetry("first failure", DateTime.UtcNow.AddSeconds(1), maxAttempts: 1);

        Assert.False(job.ScheduleRetry("terminal failure", DateTime.UtcNow.AddSeconds(2), maxAttempts: 1));
        Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        Assert.Equal("terminal failure", job.LastError);
        Assert.Null(job.LeaseToken);
    }

    [Fact]
    public void Interrupted_processing_can_be_reset_for_lease_recovery()
    {
        var version = DocumentVersion.Create(
            Guid.NewGuid(),
            1,
            "hash",
            "text/plain",
            "document.txt",
            "documents/version",
            4,
            "documents/version",
            null);

        version.BeginProcessing();
        version.RecoverInterruptedProcessing();

        Assert.Equal(DocumentProcessingState.Pending, version.ProcessingState);
        Assert.Null(version.ProcessingStartedOnUtc);
    }
}
