using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Semantics;

namespace BankingBrain.Application.Features.Semantics.Queries;

public interface ISemanticQueryService
{
    Task<SemanticEntityResponse?> FindEntityAsync(Guid workspaceId, string entityType, string name, string? externalIdentifier, CancellationToken cancellationToken);
    Task<SemanticQueryResult> QueryClaimsAsync(Guid workspaceId, SemanticClaimQuery query, CancellationToken cancellationToken);
    Task<SemanticClaim?> GetLatestClaimAsync(Guid workspaceId, SemanticClaimQuery query, CancellationToken cancellationToken);
    Task<SemanticQueryResult> QueryRelationshipsAsync(Guid workspaceId, SemanticRelationshipQuery query, CancellationToken cancellationToken);
    Task<SemanticClaimDetails?> GetEvidenceAsync(Guid workspaceId, Guid claimId, CancellationToken cancellationToken);

    /// <summary>
    /// Exact-match entity lookup (no normalized-name fuzzy fallback, unlike
    /// <see cref="FindEntityAsync"/>): matches ExternalIdentifier verbatim when supplied,
    /// otherwise CanonicalName verbatim. Suitable for code entities (Repository, Project,
    /// Namespace, Class, Interface, Struct, Record, Enum, Method, Property, Field), whose
    /// identity is a stable external identifier or exact declared name rather than a
    /// fuzzy-matched real-world name.
    /// </summary>
    Task<SemanticEntityResponse?> FindCodeEntityAsync(
        Guid workspaceId,
        string? entityType,
        string? canonicalName,
        string? externalIdentifier,
        CancellationToken cancellationToken)
        => FindCodeEntityAsync(workspaceId, entityType, canonicalName, externalIdentifier, null, cancellationToken);

    /// <summary>
    /// Same as the four-argument overload, with an additional optional <paramref name="entityTypes"/>
    /// filter (applied server-side, in addition to/independently of <paramref name="entityType"/>) for
    /// callers that need to restrict resolution to a known set of entity types up front - e.g. Code
    /// Intelligence's fixed vocabulary - without pulling non-matching-type rows into memory first.
    /// </summary>
    Task<SemanticEntityResponse?> FindCodeEntityAsync(
        Guid workspaceId,
        string? entityType,
        string? canonicalName,
        string? externalIdentifier,
        IReadOnlyCollection<string>? entityTypes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generic single-hop relationship traversal: returns every SemanticRelationship row
    /// where the given entity participates with the given predicate (exact match) in the
    /// given direction, paired with the related entity and that relationship's own
    /// evidence/source-location. One result per relationship (not deduplicated by related
    /// entity), so every distinct piece of evidence for the same fact is preserved.
    /// </summary>
    Task<IReadOnlyList<SemanticRelatedEntityResponse>> GetRelatedEntitiesAsync(
        Guid workspaceId,
        Guid entityId,
        string predicate,
        SemanticRelationshipDirection direction,
        CancellationToken cancellationToken);

    /// <summary>Types declared in a project's namespaces (two-hop: PROJECT_CONTAINS_NAMESPACE then NAMESPACE_CONTAINS_TYPE).</summary>
    Task<IReadOnlyList<SemanticEntityResponse>> GetTypesInProjectAsync(
        Guid workspaceId,
        Guid projectEntityId,
        CancellationToken cancellationToken);

    /// <summary>Methods, properties, and fields declared directly on a type.</summary>
    Task<IReadOnlyList<SemanticRelatedEntityResponse>> GetTypeMembersAsync(
        Guid workspaceId,
        Guid typeEntityId,
        CancellationToken cancellationToken);

    Task<SemanticRelationshipDetails?> GetRelationshipEvidenceAsync(
        Guid workspaceId,
        Guid relationshipId,
        CancellationToken cancellationToken);

    /// <summary>Every relationship (with evidence) where the entity appears as subject or object.</summary>
    Task<IReadOnlyList<SemanticRelationshipDetails>> GetEntityProvenanceAsync(
        Guid workspaceId,
        Guid entityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// SQL Lineage: "what depends on this column?" - every entity (other columns via a
    /// foreign key, view output columns, or reading/writing procedures/functions) with a
    /// direct (single-hop) SQL dependency on the given column, across every SQL Lineage
    /// dependency predicate. Each row carries the real relationship/evidence that backs it.
    /// </summary>
    Task<IReadOnlyList<SemanticRelatedEntityResponse>> GetSqlColumnDependentsAsync(
        Guid workspaceId,
        Guid columnEntityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// SQL Lineage: "what could break if this column changes?" - the transitive closure of
    /// <see cref="GetSqlColumnDependentsAsync"/> (and the equivalent table/procedure-call
    /// dependency predicates), breadth-first up to <paramref name="maxDepth"/> hops. Never
    /// fabricates a hop: every entity returned was reached by a real, evidence-backed
    /// relationship, carried on <see cref="SemanticImpactEntityResponse"/>.
    /// </summary>
    Task<IReadOnlyList<SemanticImpactEntityResponse>> GetSqlImpactAsync(
        Guid workspaceId,
        Guid entityId,
        int maxDepth,
        CancellationToken cancellationToken);

    /// <summary>
    /// API Intelligence: "what code path is reachable from this endpoint?" - forward
    /// traversal from an ApiEndpoint entity across its resolved handler and the existing
    /// Code Intelligence call graph, up to <paramref name="maxDepth"/> hops.
    /// </summary>
    Task<IReadOnlyList<SemanticImpactEntityResponse>> GetReachableCodePathAsync(
        Guid workspaceId,
        Guid endpointEntityId,
        int maxDepth,
        CancellationToken cancellationToken);

    /// <summary>
    /// API Intelligence: "which endpoints may be affected by a changed method?" - backward
    /// traversal from a Method entity across the same call graph, filtered to the
    /// ApiEndpoint entities found in the closure.
    /// </summary>
    Task<IReadOnlyList<SemanticImpactEntityResponse>> GetEndpointsAffectedByMethodAsync(
        Guid workspaceId,
        Guid methodEntityId,
        int maxDepth,
        CancellationToken cancellationToken);
}