using BankingBrain.Domain.Entities.Semantics;

namespace BankingBrain.Application.Features.Semantics.Queries;

public sealed record SemanticClaimQuery(
    string? Subject = null,
    string? Predicate = null,
    string? ObjectValue = null,
    DateTime? AsOfUtc = null,
    DateTime? ValidAtUtc = null,
    DateTime? ValidFromUtc = null,
    DateTime? ValidToUtc = null);

public sealed record SemanticRelationshipQuery(
    Guid? SubjectEntityId = null,
    Guid? ObjectEntityId = null,
    string? Predicate = null);

public sealed record SemanticDerivedRelationship(
    Guid SubjectEntityId,
    Guid ObjectEntityId,
    string Predicate,
    bool IsDerived,
    IReadOnlyList<Guid> SupportingClaimIds);

public sealed record SemanticQueryResult(
    IReadOnlyList<SemanticClaim> Claims,
    IReadOnlyList<SemanticEvidence> Evidence,
    IReadOnlyList<SemanticDerivedRelationship> Relationships);

/// <summary>
/// Direction of traversal relative to a given entity: Outgoing treats the entity as the
/// relationship subject (returning object-side entities), Incoming treats it as the
/// object (returning subject-side entities).
/// </summary>
public enum SemanticRelationshipDirection
{
    Outgoing = 1,
    Incoming = 2
}

public sealed record SemanticRelatedEntityResponse(
    SemanticEntityResponse Entity,
    Guid RelationshipId,
    string Predicate,
    decimal ExtractionConfidence,
    Guid EvidenceId,
    Guid DocumentVersionId,
    string SourceLocation);

public sealed record SemanticRelationshipDetails(
    SemanticRelationship Relationship,
    SemanticEvidence Evidence,
    SemanticEntity? SubjectEntity,
    SemanticEntity? ObjectEntity);

/// <summary>
/// One entity reached while walking a multi-hop impact traversal (see
/// <c>ISemanticQueryService.GetSqlImpactAsync</c>), paired with the specific relationship
/// and evidence that connected it at that hop - so "what could break" never asserts a
/// dependency without the exact source location backing it.
/// </summary>
public sealed record SemanticImpactEntityResponse(
    SemanticEntityResponse Entity,
    int Depth,
    Guid RelationshipId,
    string Predicate,
    Guid EvidenceId,
    Guid DocumentVersionId,
    string SourceLocation);