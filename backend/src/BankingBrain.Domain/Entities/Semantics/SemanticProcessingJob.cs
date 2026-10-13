using BankingBrain.Domain.Enums;

namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticProcessingJob
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid ExtractionRunId { get; private set; }
    public string ExtractorIdentity { get; private set; } = string.Empty;
    public string ExtractorVersion { get; private set; } = string.Empty;
    public ProcessingJobStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime AvailableAtUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? StartedOnUtc { get; private set; }
    public DateTime? CompletedOnUtc { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public string? LastError { get; private set; }

    private SemanticProcessingJob() { }

    public static SemanticProcessingJob Create(Guid workspaceId, Guid versionId, Guid runId, string identity, string version)
    {
        if (workspaceId == Guid.Empty || versionId == Guid.Empty || runId == Guid.Empty)
            throw new ArgumentException("Semantic jobs require workspace, version, and run identifiers.");
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Extractor identity and version are required.");
        var now = DateTime.UtcNow;
        return new SemanticProcessingJob
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, DocumentVersionId = versionId,
            ExtractionRunId = runId, ExtractorIdentity = identity.Trim(), ExtractorVersion = version.Trim(),
            Status = ProcessingJobStatus.Pending, AvailableAtUtc = now, CreatedOnUtc = now
        };
    }

    public void Claim(Guid leaseToken, DateTime leaseUntilUtc)
    {
        Status = ProcessingJobStatus.Processing;
        Attempts++;
        StartedOnUtc = DateTime.UtcNow;
        LeaseUntilUtc = leaseUntilUtc;
        LeaseToken = leaseToken;
    }

    public void Complete()
    {
        Status = ProcessingJobStatus.Completed;
        CompletedOnUtc = DateTime.UtcNow;
        LeaseUntilUtc = null;
        LeaseToken = null;
        LastError = null;
    }

    public bool ScheduleRetry(string error, DateTime availableAtUtc, int maxAttempts)
    {
        if (Attempts >= maxAttempts)
        {
            Fail(error);
            return false;
        }
        Status = ProcessingJobStatus.Pending;
        AvailableAtUtc = availableAtUtc;
        LeaseUntilUtc = null;
        LeaseToken = null;
        LastError = error;
        return true;
    }

    public void Fail(string error)
    {
        Status = ProcessingJobStatus.Failed;
        CompletedOnUtc = DateTime.UtcNow;
        LeaseUntilUtc = null;
        LeaseToken = null;
        LastError = error;
    }
}
