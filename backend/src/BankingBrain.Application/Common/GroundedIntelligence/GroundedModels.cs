namespace BankingBrain.Application.Common.GroundedIntelligence;

public sealed record GroundedQuestion(string Question, DateTime? AsOfUtc = null, DateTime? ValidAtUtc = null, int MaxGraphDepth = 1);

public sealed record QuestionIntent(
    string? Predicate,
    string? EntityName,
    bool RequiresExactClaims,
    bool RequiresGraph,
    bool RequiresSemanticSearch,
    bool RequiresCodeSearch = false);

public sealed record GroundedContextItem(
    string Kind,
    string Text,
    Guid? DocumentVersionId,
    Guid? DocumentSectionId,
    Guid? SemanticEntityId,
    Guid? SemanticClaimId,
    Guid? RelationshipId,
    Guid? EvidenceId,
    string? SourceLocation,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    DateTime? AsOfUtc,
    decimal? ExtractionConfidence,
    decimal? SourceAuthorityScore);

public sealed record GroundedContext(IReadOnlyList<GroundedContextItem> Items, QuestionIntent Intent, bool WasTruncated = false, int CharacterCount = 0);

public sealed record GroundedCitation(Guid? EvidenceId, Guid? DocumentVersionId, Guid? DocumentSectionId, string? SourceLocation, Guid? SemanticClaimId, Guid? RelationshipId);

public sealed record GroundedAnswer(string Answer, string EvidenceStatus, IReadOnlyList<GroundedCitation> Citations, IReadOnlyList<GroundedContextItem> Context, QuestionIntent Intent, string ProviderIdentity = "deterministic", string ProviderVersion = "1.0", string Model = "deterministic");