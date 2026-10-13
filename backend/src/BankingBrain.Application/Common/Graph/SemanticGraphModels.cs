namespace BankingBrain.Application.Common.Graph;

public sealed record SemanticGraphEdgeModel(
    Guid SubjectEntityId,
    Guid ObjectEntityId,
    string Predicate,
    Guid? SourceRelationshipId,
    IReadOnlyList<Guid> SupportingClaimIds,
    IReadOnlyList<Guid> EvidenceIds,
    Guid? DocumentVersionId,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    DateTime? AsOfUtc,
    decimal? ExtractionConfidence,
    decimal? SourceAuthorityScore,
    bool IsDerived,
    string IdentityHash);

public sealed record SemanticGraphQuery(
    Guid? SubjectEntityId = null,
    Guid? ObjectEntityId = null,
    string? Predicate = null,
    DateTime? AsOfUtc = null,
    DateTime? ValidAtUtc = null,
    int MaxDepth = 1);

public sealed record SemanticGraphResult(
    IReadOnlyList<SemanticGraphEdgeModel> Edges,
    IReadOnlyList<Guid> VisitedEntityIds);