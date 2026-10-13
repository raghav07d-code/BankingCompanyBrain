namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticRelationship
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid ExtractionRunId { get; private set; }
    public Guid EvidenceId { get; private set; }
    public Guid SubjectEntityId { get; private set; }
    public Guid ObjectEntityId { get; private set; }
    public string RelationType { get; private set; } = string.Empty;
    public decimal ExtractionConfidence { get; private set; }
    public string IdentityHash { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }

    private SemanticRelationship() { }

    public static SemanticRelationship Create(
        Guid workspaceId,
        Guid documentVersionId,
        Guid extractionRunId,
        Guid evidenceId,
        Guid subjectEntityId,
        Guid objectEntityId,
        string relationType,
        decimal extractionConfidence)
    {
        if (workspaceId == Guid.Empty || documentVersionId == Guid.Empty || extractionRunId == Guid.Empty || evidenceId == Guid.Empty)
            throw new ArgumentException("A relationship requires workspace, version, run, and evidence identifiers.");
        if (subjectEntityId == Guid.Empty || objectEntityId == Guid.Empty)
            throw new ArgumentException("A relationship requires subject and object entities.");
        if (string.IsNullOrWhiteSpace(relationType))
            throw new ArgumentException("Relationship type is required.", nameof(relationType));
        if (extractionConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(extractionConfidence));

        var normalized = $"{subjectEntityId:N}|{SemanticEntity.Normalize(relationType)}|{objectEntityId:N}|{evidenceId:N}";
        return new SemanticRelationship
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            DocumentVersionId = documentVersionId,
            ExtractionRunId = extractionRunId,
            EvidenceId = evidenceId,
            SubjectEntityId = subjectEntityId,
            ObjectEntityId = objectEntityId,
            RelationType = relationType.Trim(),
            ExtractionConfidence = extractionConfidence,
            IdentityHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))),
            CreatedOnUtc = DateTime.UtcNow
        };
    }
}
