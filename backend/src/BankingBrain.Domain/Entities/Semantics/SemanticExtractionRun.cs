using BankingBrain.Domain.Enums;

namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticExtractionRun
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public string ExtractorIdentity { get; private set; } = string.Empty;
    public string ExtractorVersion { get; private set; } = string.Empty;
    public SemanticExtractionRunStatus Status { get; private set; }
    public DateTime StartedOnUtc { get; private set; }
    public DateTime? CompletedOnUtc { get; private set; }
    public int EntityCount { get; private set; }
    public int ClaimCount { get; private set; }
    public int RelationshipCount { get; private set; }
    public string? ErrorCategory { get; private set; }
    public string? Error { get; private set; }

    private SemanticExtractionRun() { }

    public static SemanticExtractionRun Start(Guid workspaceId, Guid documentVersionId, string identity, string version)
    {
        if (workspaceId == Guid.Empty || documentVersionId == Guid.Empty)
            throw new ArgumentException("Semantic extraction requires workspace and version identifiers.");
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Extractor identity and version are required.");
        return new SemanticExtractionRun
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            DocumentVersionId = documentVersionId,
            ExtractorIdentity = identity.Trim(),
            ExtractorVersion = version.Trim(),
            Status = SemanticExtractionRunStatus.Pending,
            StartedOnUtc = DateTime.UtcNow
        };
    }

    public void MarkRunning() => Status = SemanticExtractionRunStatus.Running;

    public void ResetForRetry()
    {
        Status = SemanticExtractionRunStatus.Pending;
        CompletedOnUtc = null;
        ErrorCategory = null;
        Error = null;
    }

    public void Complete(int entityCount, int claimCount, int relationshipCount)
    {
        Status = SemanticExtractionRunStatus.Succeeded;
        CompletedOnUtc = DateTime.UtcNow;
        EntityCount = entityCount;
        ClaimCount = claimCount;
        RelationshipCount = relationshipCount;
        Error = null;
        ErrorCategory = null;
    }

    public void Fail(string category, string error)
    {
        Status = SemanticExtractionRunStatus.Failed;
        CompletedOnUtc = DateTime.UtcNow;
        ErrorCategory = category;
        Error = error;
    }
}
