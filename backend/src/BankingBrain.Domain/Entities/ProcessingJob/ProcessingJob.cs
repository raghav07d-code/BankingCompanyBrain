using BankingBrain.Domain.Enums;

namespace BankingBrain.Domain.Entities.ProcessingJob;

public sealed class ProcessingJob
{
    public Guid Id { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public ProcessingJobStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime AvailableAtUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? StartedOnUtc { get; private set; }
    public DateTime? CompletedOnUtc { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public string? LastError { get; private set; }

    private ProcessingJob() { }

    private ProcessingJob(Guid id, Guid documentVersionId)
    {
        Id = id;
        DocumentVersionId = documentVersionId;
        Status = ProcessingJobStatus.Pending;
        AvailableAtUtc = DateTime.UtcNow;
        CreatedOnUtc = AvailableAtUtc;
    }

    public static ProcessingJob Create(Guid documentVersionId)
    {
        if (documentVersionId == Guid.Empty)
            throw new ArgumentException("Document version ID is required.", nameof(documentVersionId));
        return new ProcessingJob(Guid.NewGuid(), documentVersionId);
    }

    public void MarkCompleted()
    {
        Status = ProcessingJobStatus.Completed;
        CompletedOnUtc = DateTime.UtcNow;
        LeaseUntilUtc = null;
        LeaseToken = null;
        LastError = null;
    }

    public void Claim(Guid leaseToken, DateTime leaseUntilUtc)
    {
        Status = ProcessingJobStatus.Processing;
        Attempts++;
        StartedOnUtc = DateTime.UtcNow;
        LeaseUntilUtc = leaseUntilUtc;
        LeaseToken = leaseToken;
    }

    public void MarkFailed(string error)
    {
        Status = ProcessingJobStatus.Failed;
        CompletedOnUtc = DateTime.UtcNow;
        LeaseUntilUtc = null;
        LeaseToken = null;
        LastError = error;
    }

    public bool ScheduleRetry(string error, DateTime availableAtUtc, int maxAttempts)
    {
        if (Attempts >= maxAttempts)
        {
            MarkFailed(error);
            return false;
        }

        Status = ProcessingJobStatus.Pending;
        AvailableAtUtc = availableAtUtc;
        LeaseUntilUtc = null;
        LeaseToken = null;
        LastError = error;
        return true;
    }
}