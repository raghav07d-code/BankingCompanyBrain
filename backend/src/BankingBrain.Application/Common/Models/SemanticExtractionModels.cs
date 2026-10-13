namespace BankingBrain.Application.Common.Models;

public sealed record SemanticExtractionInput(
    Guid WorkspaceId,
    Guid DocumentVersionId,
    IReadOnlyList<NormalizedSection> Sections);

public sealed record SemanticExtractionResult(
    IReadOnlyList<SemanticEntityCandidate> Entities,
    IReadOnlyList<SemanticClaimCandidate> Claims,
    IReadOnlyList<SemanticRelationshipCandidate> Relationships);

public sealed record SemanticEntityCandidate(
    string EntityType,
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    decimal Confidence,
    int EvidenceSequence,
    string? ExternalIdentifier = null);

public sealed record SemanticClaimCandidate(
    string Subject,
    string Predicate,
    string ObjectValue,
    decimal Confidence,
    int EvidenceSequence,
    string? SubjectEntityType = null,
    string? ObjectEntityType = null,
    DateTime? ValidFromUtc = null,
    DateTime? ValidToUtc = null,
    DateTime? AsOfUtc = null,
    DateTime? SourcePublishedOnUtc = null,
    string ValueKind = "text",
    decimal? NumericValue = null,
    string? CurrencyCode = null,
    string? Unit = null,
    DateTime? PeriodStartUtc = null,
    DateTime? PeriodEndUtc = null,
    decimal? SourceAuthorityScore = null);

public sealed record SemanticRelationshipCandidate(
    string Subject,
    string Predicate,
    string Object,
    decimal Confidence,
    int EvidenceSequence,
    string SubjectEntityType = "Entity",
    string ObjectEntityType = "Entity");
