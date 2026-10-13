using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.Semantics.Queries;

public sealed record SemanticRunResponse(Guid RunId, Guid DocumentVersionId, string ExtractorIdentity, string ExtractorVersion, SemanticExtractionRunStatus Status, DateTime StartedOnUtc, DateTime? CompletedOnUtc, int EntityCount, int ClaimCount, int RelationshipCount, string? ErrorCategory);
public sealed record SemanticEntityResponse(Guid EntityId, Guid WorkspaceId, string EntityType, string CanonicalName, string? Aliases);
public sealed record SemanticClaimResponse(Guid ClaimId, Guid DocumentVersionId, Guid? SubjectEntityId, Guid? ObjectEntityId, string Subject, string Predicate, string ObjectValue, decimal ExtractionConfidence, decimal? SourceAuthorityScore, SemanticClaimStatus Status, Guid EvidenceId, DateTime? ValidFromUtc, DateTime? ValidToUtc, DateTime? AsOfUtc, DateTime? SourcePublishedOnUtc, string ValueKind, decimal? NumericValue, string? CurrencyCode, string? Unit, DateTime? PeriodStartUtc, DateTime? PeriodEndUtc);
public sealed record SemanticEntityTypeResponse(Guid EntityTypeId, string Code, string DisplayName, string? Description);
public sealed record SemanticPredicateResponse(Guid PredicateId, string Code, string DisplayName, bool AllowsEntityObject, string AllowedSubjectTypes);
public sealed record SemanticEvidenceResponse(Guid EvidenceId, Guid DocumentVersionId, Guid? DocumentSectionId, string SourceLocation, int? PageNumber, string? SheetName, int? SlideNumber, int? RowNumber, int? ColumnNumber, string? CellReference, string? ExtractedText, string? ExtractedValue);
public sealed record SemanticClaimDetailsResponse(SemanticClaimResponse Claim, SemanticEvidenceResponse Evidence, SemanticEntityResponse? SubjectEntity, SemanticEntityResponse? ObjectEntity);
public sealed record SemanticQueryResponse(IReadOnlyList<SemanticClaimResponse> Claims, IReadOnlyList<SemanticEvidenceResponse> Evidence, IReadOnlyList<SemanticDerivedRelationship> Relationships);
public sealed record SemanticRelationshipResponse(Guid RelationshipId, Guid WorkspaceId, Guid DocumentVersionId, Guid SubjectEntityId, Guid ObjectEntityId, string Predicate, decimal ExtractionConfidence, Guid EvidenceId);
public sealed record SemanticRelationshipDetailsResponse(SemanticRelationshipResponse Relationship, SemanticEvidenceResponse Evidence, SemanticEntityResponse? SubjectEntity, SemanticEntityResponse? ObjectEntity);
