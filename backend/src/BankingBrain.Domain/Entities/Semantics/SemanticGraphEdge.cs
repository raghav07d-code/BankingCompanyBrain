namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticGraphEdge
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid SubjectEntityId { get; private set; }
    public Guid ObjectEntityId { get; private set; }
    public string Predicate { get; private set; } = string.Empty;
    public Guid? SourceRelationshipId { get; private set; }
    public string SupportingClaimIdsJson { get; private set; } = "[]";
    public string EvidenceIdsJson { get; private set; } = "[]";
    public Guid? DocumentVersionId { get; private set; }
    public DateTime? ValidFromUtc { get; private set; }
    public DateTime? ValidToUtc { get; private set; }
    public DateTime? AsOfUtc { get; private set; }
    public decimal? ExtractionConfidence { get; private set; }
    public decimal? SourceAuthorityScore { get; private set; }
    public bool IsDerived { get; private set; }
    public string IdentityHash { get; private set; } = string.Empty;
    public DateTime ProjectedOnUtc { get; private set; }

    private SemanticGraphEdge() { }

    public static SemanticGraphEdge Create(Guid workspaceId, Guid subjectEntityId, Guid objectEntityId, string predicate, Guid? sourceRelationshipId, string supportingClaimIdsJson, string evidenceIdsJson, Guid? documentVersionId, DateTime? validFromUtc, DateTime? validToUtc, DateTime? asOfUtc, decimal? extractionConfidence, decimal? sourceAuthorityScore, bool isDerived, string identityHash) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = workspaceId, SubjectEntityId = subjectEntityId, ObjectEntityId = objectEntityId,
        Predicate = predicate.Trim().ToUpperInvariant(), SourceRelationshipId = sourceRelationshipId, SupportingClaimIdsJson = supportingClaimIdsJson,
        EvidenceIdsJson = evidenceIdsJson, DocumentVersionId = documentVersionId, ValidFromUtc = validFromUtc, ValidToUtc = validToUtc,
        AsOfUtc = asOfUtc, ExtractionConfidence = extractionConfidence, SourceAuthorityScore = sourceAuthorityScore, IsDerived = isDerived,
        IdentityHash = identityHash, ProjectedOnUtc = DateTime.UtcNow
    };
}