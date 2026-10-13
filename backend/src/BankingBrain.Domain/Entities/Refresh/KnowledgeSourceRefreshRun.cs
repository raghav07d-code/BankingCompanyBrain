using BankingBrain.Domain.Enums;

namespace BankingBrain.Domain.Entities.Refresh;

/// <summary>
/// One queued request to refresh a workspace-scoped semantic extractor (Code Intelligence,
/// API Intelligence, ...) for a knowledge source, bound to the exact immutable Git revision
/// it must read - never "whatever the connector's working copy currently contains". This is
/// the explicit, observable state machine callers (including evaluation tooling) use to tell
/// queued/running/completed/failed/superseded work apart, instead of inferring completion
/// from a global extraction-run count that says nothing about which specific request it was.
/// </summary>
public sealed class KnowledgeSourceRefreshRun
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid KnowledgeSourceId { get; private set; }
    public string ExtractorIdentity { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public KnowledgeSourceRefreshRunStatus Status { get; private set; }
    public DateTime QueuedOnUtc { get; private set; }
    public DateTime? StartedOnUtc { get; private set; }
    public DateTime? CompletedOnUtc { get; private set; }
    public string? ErrorCategory { get; private set; }
    public string? Error { get; private set; }

    private KnowledgeSourceRefreshRun()
    {
    }

    public static KnowledgeSourceRefreshRun Queue(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string extractorIdentity,
        string revision)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));

        if (knowledgeSourceId == Guid.Empty)
            throw new ArgumentException("Knowledge source ID is required.", nameof(knowledgeSourceId));

        ArgumentException.ThrowIfNullOrWhiteSpace(extractorIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        return new KnowledgeSourceRefreshRun
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            KnowledgeSourceId = knowledgeSourceId,
            ExtractorIdentity = extractorIdentity.Trim(),
            Revision = revision.Trim(),
            Status = KnowledgeSourceRefreshRunStatus.Queued,
            QueuedOnUtc = DateTime.UtcNow
        };
    }

    public void MarkRunning()
    {
        if (Status is KnowledgeSourceRefreshRunStatus.Superseded)
            return;

        Status = KnowledgeSourceRefreshRunStatus.Running;
        StartedOnUtc = DateTime.UtcNow;
    }

    public void Complete()
    {
        Status = KnowledgeSourceRefreshRunStatus.Completed;
        CompletedOnUtc = DateTime.UtcNow;
    }

    public void Fail(string category, string error)
    {
        Status = KnowledgeSourceRefreshRunStatus.Failed;
        ErrorCategory = category;
        Error = error;
        CompletedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks a still-pending (Queued or Running) run as superseded by a newer request for
    /// the same (workspace, knowledge source, extractor). A terminal run (Completed/Failed)
    /// is left alone - its result is already a fact about the revision it was bound to, and
    /// nothing about a later request changes that.
    /// </summary>
    public void Supersede()
    {
        if (Status is KnowledgeSourceRefreshRunStatus.Queued or KnowledgeSourceRefreshRunStatus.Running)
        {
            Status = KnowledgeSourceRefreshRunStatus.Superseded;
            CompletedOnUtc = DateTime.UtcNow;
        }
    }
}
