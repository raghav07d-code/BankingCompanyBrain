using BankingBrain.Domain.Enums;

namespace BankingBrain.Domain.Entities.KnowledgeSource;

public sealed class KnowledgeSource
{
    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public KnowledgeSourceType SourceType { get; private set; }

    public string? ConnectorKey { get; private set; }

    public string? ExternalIdentifier { get; private set; }

    /// <summary>
    /// The exact Git revision whose content was last written to an immutable, revision-keyed
    /// snapshot directory by reconciliation. Analyzers that read repository files (Code
    /// Intelligence, API Intelligence, Data Access Intelligence) resolve their input from
    /// this pinned revision instead of the connector's live, mutable working copy - a later
    /// commit changing or deleting a file on disk cannot affect an extraction already bound
    /// to an earlier revision's immutable copy.
    /// </summary>
    public string? LastSnapshotRevision { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    private KnowledgeSource()
    {
    }

    private KnowledgeSource(
        Guid id,
        Guid workspaceId,
        string name,
        string? description,
        KnowledgeSourceType sourceType,
        string? connectorKey,
        string? externalIdentifier)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Name = name;
        Description = description;
        SourceType = sourceType;
        ConnectorKey = connectorKey;
        ExternalIdentifier = externalIdentifier;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = CreatedOnUtc;
    }

    public static KnowledgeSource Create(
        Guid workspaceId,
        string name,
        string? description,
        KnowledgeSourceType sourceType,
        string? connectorKey = null,
        string? externalIdentifier = null)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException(
                "Workspace ID is required.",
                nameof(workspaceId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Knowledge source name is required.",
                nameof(name));

        if (!Enum.IsDefined(sourceType))
            throw new ArgumentOutOfRangeException(
                nameof(sourceType));

        connectorKey = string.IsNullOrWhiteSpace(connectorKey)
            ? null
            : connectorKey.Trim();

        externalIdentifier = string.IsNullOrWhiteSpace(externalIdentifier)
            ? null
            : externalIdentifier.Trim();

        return new KnowledgeSource(
            Guid.NewGuid(),
            workspaceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(description)
                ? null
                : description.Trim(),
            sourceType,
            connectorKey,
            externalIdentifier);
    }

    public void SetLastSnapshotRevision(string revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        LastSnapshotRevision = revision.Trim();
        UpdatedOnUtc = DateTime.UtcNow;
    }
}