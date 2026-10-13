namespace BankingBrain.Domain.Entities.Document;

public sealed class Document
{
    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid KnowledgeSourceId { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? ExternalIdentifier { get; private set; }

    public string MimeType { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    private Document()
    {
    }

    private Document(
        Guid id,
        Guid workspaceId,
        Guid knowledgeSourceId,
        string name,
        string? externalIdentifier,
        string mimeType)
    {
        Id = id;
        WorkspaceId = workspaceId;
        KnowledgeSourceId = knowledgeSourceId;
        Name = name;
        ExternalIdentifier = externalIdentifier;
        MimeType = mimeType;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = CreatedOnUtc;
    }

    public static Document Create(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string name,
        string? externalIdentifier,
        string mimeType)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException(
                "Workspace ID is required.",
                nameof(workspaceId));

        if (knowledgeSourceId == Guid.Empty)
            throw new ArgumentException(
                "Knowledge source ID is required.",
                nameof(knowledgeSourceId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Document name is required.",
                nameof(name));

        if (string.IsNullOrWhiteSpace(mimeType))
            throw new ArgumentException(
                "Document MIME type is required.",
                nameof(mimeType));

        return new Document(
            Guid.NewGuid(),
            workspaceId,
            knowledgeSourceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(externalIdentifier)
                ? null
                : externalIdentifier.Trim(),
            mimeType.Trim());
    }

    public void SetCurrentVersion(Guid versionId)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException(
                "Version ID is required.",
                nameof(versionId));

        CurrentVersionId = versionId;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        IsActive = true;
        UpdatedOnUtc = DateTime.UtcNow;
    }
}