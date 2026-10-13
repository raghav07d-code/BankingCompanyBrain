using System.Data;
using System.Diagnostics;
using BankingBrain.Application.Features.Semantics.Queries;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class SemanticQueryService : ISemanticQueryService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SemanticQueryService> _logger;

    public SemanticQueryService(ApplicationDbContext db, ILogger<SemanticQueryService> logger) { _db = db; _logger = logger; }

    public async Task<SemanticEntityResponse?> FindEntityAsync(Guid workspaceId, string entityType, string name, string? externalIdentifier, CancellationToken cancellationToken)
    {
        var normalizedName = SemanticEntity.Normalize(name);
        var query = _db.SemanticEntities.AsNoTracking().Where(entity =>
            entity.WorkspaceId == workspaceId && entity.EntityType == entityType.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(externalIdentifier))
            query = query.Where(entity => entity.ExternalIdentifier == externalIdentifier.Trim());
        var candidates = await query.ToListAsync(cancellationToken);
        var entity = string.IsNullOrWhiteSpace(externalIdentifier)
            ? candidates.FirstOrDefault(item => item.NormalizedName == normalizedName || (item.Aliases ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
            : candidates.FirstOrDefault();
        return entity is null ? null : new SemanticEntityResponse(entity.Id, entity.WorkspaceId, entity.EntityType, entity.CanonicalName, entity.Aliases);
    }

    public async Task<SemanticQueryResult> QueryClaimsAsync(Guid workspaceId, SemanticClaimQuery query, CancellationToken cancellationToken)
    {
        var claimsQuery = _db.SemanticClaims.AsNoTracking().Where(claim => claim.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(query.Subject)) claimsQuery = claimsQuery.Where(claim => claim.Subject == query.Subject.Trim());
        if (!string.IsNullOrWhiteSpace(query.Predicate)) claimsQuery = claimsQuery.Where(claim => claim.Predicate.ToUpper() == query.Predicate.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(query.ObjectValue)) claimsQuery = claimsQuery.Where(claim => claim.ObjectValue == query.ObjectValue.Trim());
        if (query.AsOfUtc is not null) claimsQuery = claimsQuery.Where(claim => claim.AsOfUtc == null || claim.AsOfUtc <= query.AsOfUtc);
        if (query.ValidAtUtc is not null) claimsQuery = claimsQuery.Where(claim => (claim.ValidFromUtc == null || claim.ValidFromUtc <= query.ValidAtUtc) && (claim.ValidToUtc == null || claim.ValidToUtc >= query.ValidAtUtc));
        if (query.ValidFromUtc is not null) claimsQuery = claimsQuery.Where(claim => claim.ValidToUtc == null || claim.ValidToUtc >= query.ValidFromUtc);
        if (query.ValidToUtc is not null) claimsQuery = claimsQuery.Where(claim => claim.ValidFromUtc == null || claim.ValidFromUtc <= query.ValidToUtc);

        var claims = await claimsQuery.OrderByDescending(claim => claim.AsOfUtc ?? claim.CreatedOnUtc).ThenByDescending(claim => claim.ExtractionConfidence).ThenByDescending(claim => claim.SourceAuthorityScore ?? 0).ToListAsync(cancellationToken);
        return await BuildResultAsync(workspaceId, claims, Array.Empty<SemanticDerivedRelationship>(), cancellationToken);
    }

    public async Task<SemanticClaim?> GetLatestClaimAsync(Guid workspaceId, SemanticClaimQuery query, CancellationToken cancellationToken)
    {
        var result = await QueryClaimsAsync(workspaceId, query, cancellationToken);
        return result.Claims.FirstOrDefault();
    }

    public async Task<SemanticQueryResult> QueryRelationshipsAsync(Guid workspaceId, SemanticRelationshipQuery query, CancellationToken cancellationToken)
    {
        var currentVersionIds = CurrentDocumentVersionFilter.ActiveCurrentVersionIds(_db, workspaceId);

        var relationships = await _db.SemanticRelationships.AsNoTracking()
            .Where(relationship => relationship.WorkspaceId == workspaceId &&
                (query.SubjectEntityId == null || relationship.SubjectEntityId == query.SubjectEntityId) &&
                (query.ObjectEntityId == null || relationship.ObjectEntityId == query.ObjectEntityId) &&
                (query.Predicate == null || relationship.RelationType.ToUpper() == query.Predicate.Trim().ToUpper()) &&
                currentVersionIds.Contains(relationship.DocumentVersionId))
            .ToListAsync(cancellationToken);

        var claims = await _db.SemanticClaims.AsNoTracking()
            .Where(claim => claim.WorkspaceId == workspaceId && claim.SubjectEntityId != null && claim.ObjectEntityId != null &&
                (query.SubjectEntityId == null || claim.SubjectEntityId == query.SubjectEntityId) &&
                (query.ObjectEntityId == null || claim.ObjectEntityId == query.ObjectEntityId) &&
                (query.Predicate == null || claim.Predicate.ToUpper() == query.Predicate.Trim().ToUpper()))
            .ToListAsync(cancellationToken);

        var explicitRelationshipPredicates = await _db.SemanticPredicates.AsNoTracking()
            .Where(predicate => predicate.WorkspaceId == workspaceId && predicate.AllowsEntityObject)
            .Select(predicate => predicate.Code)
            .ToListAsync(cancellationToken);
        var derived = claims
            .Where(claim => explicitRelationshipPredicates.Contains(claim.Predicate.ToUpperInvariant()))
            .GroupBy(claim => new { Subject = claim.SubjectEntityId!.Value, Object = claim.ObjectEntityId!.Value, Predicate = claim.Predicate.ToUpperInvariant() })
            .Select(group => new SemanticDerivedRelationship(group.Key.Subject, group.Key.Object, group.Key.Predicate, true, group.Select(claim => claim.Id).ToList()))
            .ToList();
        var sourceBacked = relationships.Select(relationship => new SemanticDerivedRelationship(relationship.SubjectEntityId, relationship.ObjectEntityId, relationship.RelationType, false, Array.Empty<Guid>()));
        return await BuildResultAsync(workspaceId, claims, sourceBacked.Concat(derived).ToList(), cancellationToken);
    }

    public async Task<SemanticClaimDetails?> GetEvidenceAsync(Guid workspaceId, Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await _db.SemanticClaims.AsNoTracking().FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == claimId, cancellationToken);
        if (claim is null) return null;
        var evidence = await _db.SemanticEvidence.AsNoTracking().FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == claim.EvidenceId, cancellationToken);
        if (evidence is null) return null;
        var subject = claim.SubjectEntityId == null ? null : await _db.SemanticEntities.AsNoTracking().FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == claim.SubjectEntityId, cancellationToken);
        var objectEntity = claim.ObjectEntityId == null ? null : await _db.SemanticEntities.AsNoTracking().FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == claim.ObjectEntityId, cancellationToken);
        return new SemanticClaimDetails(claim, evidence, subject, objectEntity);
    }

    private async Task<SemanticQueryResult> BuildResultAsync(Guid workspaceId, IReadOnlyList<SemanticClaim> claims, IReadOnlyList<SemanticDerivedRelationship> relationships, CancellationToken cancellationToken)
    {
        var evidenceIds = claims.Select(claim => claim.EvidenceId).Distinct().ToList();
        var evidence = await _db.SemanticEvidence.AsNoTracking().Where(item => item.WorkspaceId == workspaceId && evidenceIds.Contains(item.Id)).ToListAsync(cancellationToken);
        return new SemanticQueryResult(claims, evidence, relationships);
    }

    /// <summary>
    /// Exact-match code entity resolution, in strict priority order - never approximate or
    /// similarity-based. Each tier is only attempted if the previous one found zero matches;
    /// a tier finding more than one match is ambiguous and resolution stops with no result
    /// (never guesses):
    ///   1. ExternalIdentifier verbatim (when supplied - bypasses the remaining tiers, since
    ///      external identifiers are already unique by construction).
    ///   2. CanonicalName verbatim - covers fully qualified type names and fully qualified
    ///      method names given with their exact parameter list.
    ///   3. CanonicalName with its trailing "(parameters)" removed - covers fully qualified
    ///      method names given without parameter types.
    ///   4. A namespace-qualified or "Type.Method" trailing-segment match against the
    ///      parameter-free qualified name (only attempted for candidates containing '.').
    ///   5. A unique simple name match (only attempted for candidates without '.'): resolves
    ///      only when exactly one code entity in the workspace has that simple name.
    /// </summary>
    public async Task<SemanticEntityResponse?> FindCodeEntityAsync(Guid workspaceId, string? entityType, string? canonicalName, string? externalIdentifier, IReadOnlyCollection<string>? entityTypes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(canonicalName) && string.IsNullOrWhiteSpace(externalIdentifier))
            return null;

        var currentEntityIds = CurrentDocumentVersionFilter.CurrentEntityIds(_db, workspaceId);

        var baseQuery = _db.SemanticEntities.AsNoTracking()
            .Where(entity => entity.WorkspaceId == workspaceId)
            .Where(entity => currentEntityIds.Contains(entity.Id));

        if (!string.IsNullOrWhiteSpace(entityType))
            baseQuery = baseQuery.Where(entity => entity.EntityType == entityType.Trim());

        // Narrows the server-side candidate set up front when the caller already knows the
        // fixed vocabulary of acceptable entity types (e.g. Code Intelligence's known types),
        // instead of materializing every entity type in the workspace only to discard the
        // non-matching ones after the fact.
        if (entityTypes is { Count: > 0 })
            baseQuery = baseQuery.Where(entity => entityTypes.Contains(entity.EntityType));

        if (!string.IsNullOrWhiteSpace(externalIdentifier))
        {
            var byExternalId = await baseQuery.FirstOrDefaultAsync(entity => entity.ExternalIdentifier == externalIdentifier.Trim(), cancellationToken);
            return byExternalId is null ? null : ToEntityResponse(byExternalId);
        }

        var trimmedName = canonicalName!.Trim();

        // Diagnostic-only: isolates "waiting for a pooled connection" from "Postgres executing
        // the query" for this request-path lookup, to distinguish connection-pool contention
        // from genuine query cost without guessing. Never logs the name/question being
        // resolved - only elapsed milliseconds and row counts.
        var connection = _db.Database.GetDbConnection();
        var connectStopwatch = Stopwatch.StartNew();
        if (connection.State != ConnectionState.Open) await _db.Database.OpenConnectionAsync(cancellationToken);
        var connectionAcquireMs = connectStopwatch.Elapsed.TotalMilliseconds;

        var queryStopwatch = Stopwatch.StartNew();
        var exact = await baseQuery.Where(entity => entity.CanonicalName == trimmedName).ToListAsync(cancellationToken);
        var exactQueryMs = queryStopwatch.Elapsed.TotalMilliseconds;

        _logger.LogInformation(
            "DbDiagnostic Operation={Operation} ConnectionAcquireMs={ConnectionAcquireMs} QueryMs={QueryMs} RowCount={RowCount}",
            "FindCodeEntity.ExactTier", connectionAcquireMs, exactQueryMs, exact.Count);

        if (exact.Count == 1) return ToEntityResponse(exact[0]);
        if (exact.Count > 1) return null;

        // Constructors are recorded as Method entities whose own simple name is always
        // identical to their declaring type's simple name (Namespace.Type.Type(args)) -
        // any type with an explicit constructor would otherwise make the fuzzy tiers
        // below "ambiguous" between the type and its own constructor for every lookup
        // by simple/suffix name, defeating exact-name resolution for the overwhelmingly
        // common case of asking about the type itself.
        var candidatesStopwatch = Stopwatch.StartNew();
        var candidates = (await baseQuery.ToListAsync(cancellationToken))
            .Where(entity => !IsConstructor(entity))
            .ToList();
        _logger.LogInformation(
            "DbDiagnostic Operation={Operation} QueryMs={QueryMs} RowCount={RowCount}",
            "FindCodeEntity.CandidatesTier", candidatesStopwatch.Elapsed.TotalMilliseconds, candidates.Count);

        var withoutParameters = candidates.Where(entity => StripParameterList(entity.CanonicalName) == trimmedName).ToList();
        if (withoutParameters.Count == 1) return ToEntityResponse(withoutParameters[0]);
        if (withoutParameters.Count > 1) return null;

        if (trimmedName.Contains('.'))
        {
            var suffix = "." + trimmedName;
            var suffixMatches = candidates.Where(entity => StripParameterList(entity.CanonicalName).EndsWith(suffix, StringComparison.Ordinal)).ToList();
            if (suffixMatches.Count == 1) return ToEntityResponse(suffixMatches[0]);
            if (suffixMatches.Count > 1) return null;
        }
        else
        {
            var simpleMatches = candidates.Where(entity => GetSimpleName(entity.CanonicalName) == trimmedName).ToList();
            if (simpleMatches.Count == 1) return ToEntityResponse(simpleMatches[0]);
            if (simpleMatches.Count > 1) return null;
        }

        return null;
    }

    private static string StripParameterList(string canonicalName)
    {
        var parenIndex = canonicalName.IndexOf('(');
        return parenIndex < 0 ? canonicalName : canonicalName[..parenIndex];
    }

    private static string GetSimpleName(string canonicalName)
    {
        var withoutParameters = StripParameterList(canonicalName);
        var lastDot = withoutParameters.LastIndexOf('.');
        return lastDot < 0 ? withoutParameters : withoutParameters[(lastDot + 1)..];
    }

    private static bool IsConstructor(SemanticEntity entity)
    {
        if (entity.EntityType != "Method")
            return false;

        var withoutParameters = StripParameterList(entity.CanonicalName);
        var lastDot = withoutParameters.LastIndexOf('.');

        if (lastDot < 0)
            return false;

        var methodName = withoutParameters[(lastDot + 1)..];
        var containingType = withoutParameters[..lastDot];
        var secondLastDot = containingType.LastIndexOf('.');
        var typeName = secondLastDot < 0 ? containingType : containingType[(secondLastDot + 1)..];

        return methodName == typeName;
    }

    public async Task<IReadOnlyList<SemanticRelatedEntityResponse>> GetRelatedEntitiesAsync(Guid workspaceId, Guid entityId, string predicate, SemanticRelationshipDirection direction, CancellationToken cancellationToken)
    {
        var normalizedPredicate = predicate.Trim().ToUpperInvariant();

        var currentVersionIds = CurrentDocumentVersionFilter.ActiveCurrentVersionIds(_db, workspaceId);

        var relationshipsQuery = _db.SemanticRelationships.AsNoTracking()
            .Where(relationship => relationship.WorkspaceId == workspaceId && relationship.RelationType.ToUpper() == normalizedPredicate)
            .Where(relationship => currentVersionIds.Contains(relationship.DocumentVersionId));

        relationshipsQuery = direction == SemanticRelationshipDirection.Outgoing
            ? relationshipsQuery.Where(relationship => relationship.SubjectEntityId == entityId)
            : relationshipsQuery.Where(relationship => relationship.ObjectEntityId == entityId);

        var relationships = await relationshipsQuery.ToListAsync(cancellationToken);

        if (relationships.Count == 0)
            return Array.Empty<SemanticRelatedEntityResponse>();

        var relatedEntityIds = relationships
            .Select(relationship => direction == SemanticRelationshipDirection.Outgoing ? relationship.ObjectEntityId : relationship.SubjectEntityId)
            .Distinct()
            .ToList();

        var entityById = (await _db.SemanticEntities.AsNoTracking()
                .Where(entity => entity.WorkspaceId == workspaceId && relatedEntityIds.Contains(entity.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(entity => entity.Id);

        var evidenceIds = relationships.Select(relationship => relationship.EvidenceId).Distinct().ToList();
        var evidenceById = (await _db.SemanticEvidence.AsNoTracking()
                .Where(evidence => evidence.WorkspaceId == workspaceId && evidenceIds.Contains(evidence.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(evidence => evidence.Id);

        var results = new List<SemanticRelatedEntityResponse>();

        foreach (var relationship in relationships)
        {
            var relatedId = direction == SemanticRelationshipDirection.Outgoing ? relationship.ObjectEntityId : relationship.SubjectEntityId;

            if (!entityById.TryGetValue(relatedId, out var relatedEntity))
                continue;

            evidenceById.TryGetValue(relationship.EvidenceId, out var evidence);

            results.Add(new SemanticRelatedEntityResponse(
                ToEntityResponse(relatedEntity),
                relationship.Id,
                relationship.RelationType,
                relationship.ExtractionConfidence,
                relationship.EvidenceId,
                relationship.DocumentVersionId,
                evidence?.SourceLocation ?? string.Empty));
        }

        return results
            .OrderBy(result => result.Entity.CanonicalName, StringComparer.Ordinal)
            .ThenBy(result => result.SourceLocation, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<SemanticEntityResponse>> GetTypesInProjectAsync(Guid workspaceId, Guid projectEntityId, CancellationToken cancellationToken)
    {
        var namespaces = await GetRelatedEntitiesAsync(workspaceId, projectEntityId, "PROJECT_CONTAINS_NAMESPACE", SemanticRelationshipDirection.Outgoing, cancellationToken);

        var seenTypeIds = new HashSet<Guid>();
        var types = new List<SemanticEntityResponse>();

        foreach (var namespaceId in namespaces.Select(item => item.Entity.EntityId).Distinct())
        {
            var namespaceTypes = await GetRelatedEntitiesAsync(workspaceId, namespaceId, "NAMESPACE_CONTAINS_TYPE", SemanticRelationshipDirection.Outgoing, cancellationToken);

            foreach (var type in namespaceTypes)
            {
                if (seenTypeIds.Add(type.Entity.EntityId))
                    types.Add(type.Entity);
            }
        }

        return types.OrderBy(type => type.CanonicalName, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<SemanticRelatedEntityResponse>> GetTypeMembersAsync(Guid workspaceId, Guid typeEntityId, CancellationToken cancellationToken)
    {
        var methods = await GetRelatedEntitiesAsync(workspaceId, typeEntityId, "TYPE_CONTAINS_METHOD", SemanticRelationshipDirection.Outgoing, cancellationToken);
        var properties = await GetRelatedEntitiesAsync(workspaceId, typeEntityId, "TYPE_CONTAINS_PROPERTY", SemanticRelationshipDirection.Outgoing, cancellationToken);
        var fields = await GetRelatedEntitiesAsync(workspaceId, typeEntityId, "TYPE_CONTAINS_FIELD", SemanticRelationshipDirection.Outgoing, cancellationToken);

        return methods.Concat(properties).Concat(fields)
            .OrderBy(member => member.Entity.EntityType, StringComparer.Ordinal)
            .ThenBy(member => member.Entity.CanonicalName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SemanticRelationshipDetails?> GetRelationshipEvidenceAsync(Guid workspaceId, Guid relationshipId, CancellationToken cancellationToken)
    {
        var relationship = await _db.SemanticRelationships.AsNoTracking()
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == relationshipId, cancellationToken);

        if (relationship is null)
            return null;

        var evidence = await _db.SemanticEvidence.AsNoTracking()
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == relationship.EvidenceId, cancellationToken);

        if (evidence is null)
            return null;

        var subject = await _db.SemanticEntities.AsNoTracking().FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == relationship.SubjectEntityId, cancellationToken);
        var objectEntity = await _db.SemanticEntities.AsNoTracking().FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.Id == relationship.ObjectEntityId, cancellationToken);

        return new SemanticRelationshipDetails(relationship, evidence, subject, objectEntity);
    }

    public async Task<IReadOnlyList<SemanticRelationshipDetails>> GetEntityProvenanceAsync(Guid workspaceId, Guid entityId, CancellationToken cancellationToken)
    {
        var currentVersionIds = CurrentDocumentVersionFilter.ActiveCurrentVersionIds(_db, workspaceId);

        var relationships = await _db.SemanticRelationships.AsNoTracking()
            .Where(relationship => relationship.WorkspaceId == workspaceId && (relationship.SubjectEntityId == entityId || relationship.ObjectEntityId == entityId))
            .Where(relationship => currentVersionIds.Contains(relationship.DocumentVersionId))
            .ToListAsync(cancellationToken);

        if (relationships.Count == 0)
            return Array.Empty<SemanticRelationshipDetails>();

        var evidenceIds = relationships.Select(relationship => relationship.EvidenceId).Distinct().ToList();
        var evidenceById = (await _db.SemanticEvidence.AsNoTracking()
                .Where(evidence => evidence.WorkspaceId == workspaceId && evidenceIds.Contains(evidence.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(evidence => evidence.Id);

        var entityIds = relationships.SelectMany(relationship => new[] { relationship.SubjectEntityId, relationship.ObjectEntityId }).Distinct().ToList();
        var entityById = (await _db.SemanticEntities.AsNoTracking()
                .Where(entity => entity.WorkspaceId == workspaceId && entityIds.Contains(entity.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(entity => entity.Id);

        var results = new List<SemanticRelationshipDetails>();

        foreach (var relationship in relationships)
        {
            if (!evidenceById.TryGetValue(relationship.EvidenceId, out var evidence))
                continue;

            entityById.TryGetValue(relationship.SubjectEntityId, out var subject);
            entityById.TryGetValue(relationship.ObjectEntityId, out var objectEntity);

            results.Add(new SemanticRelationshipDetails(relationship, evidence, subject, objectEntity));
        }

        return results
            .OrderBy(result => result.Evidence.SourceLocation, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>SQL Lineage dependency predicates considered at every hop of an impact walk - see <see cref="GetSqlImpactAsync"/>.</summary>
    private static readonly string[] SqlDependencyPredicates =
    {
        "COLUMN_DEPENDS_ON_COLUMN", "VIEW_DERIVES_FROM_COLUMN", "VIEW_DERIVES_FROM_TABLE",
        "PROCEDURE_READS_COLUMN", "PROCEDURE_WRITES_COLUMN", "PROCEDURE_READS_TABLE",
        "PROCEDURE_WRITES_TABLE", "PROCEDURE_CALLS_PROCEDURE", "FUNCTION_READS_COLUMN", "FUNCTION_READS_TABLE"
    };

    public async Task<IReadOnlyList<SemanticRelatedEntityResponse>> GetSqlColumnDependentsAsync(Guid workspaceId, Guid columnEntityId, CancellationToken cancellationToken)
    {
        var results = new List<SemanticRelatedEntityResponse>();
        foreach (var predicate in SqlDependencyPredicates)
            results.AddRange(await GetRelatedEntitiesAsync(workspaceId, columnEntityId, predicate, SemanticRelationshipDirection.Incoming, cancellationToken));

        return results
            .OrderBy(result => result.Entity.CanonicalName, StringComparer.Ordinal)
            .ThenBy(result => result.SourceLocation, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Code/API dependency predicates considered when walking a code-call-chain BFS - see <see cref="GetReachableCodePathAsync"/>/<see cref="GetEndpointsAffectedByMethodAsync"/>.</summary>
    /// <summary>
    /// Includes UNRESOLVED_REFERENCE deliberately: a workflow trace should surface "this
    /// call could not be statically resolved" as a terminal, explicitly-marked node in the
    /// same traversal, not silently stop with no indication anything was there.
    /// </summary>
    private static readonly string[] CodeCallChainPredicates =
    {
        "ENDPOINT_INVOKES_METHOD", "CALLS", "METHOD_DEPENDS_ON_TYPE", "METHOD_ACCESSES_TABLE", "UNRESOLVED_REFERENCE"
    };

    public Task<IReadOnlyList<SemanticImpactEntityResponse>> GetSqlImpactAsync(Guid workspaceId, Guid entityId, int maxDepth, CancellationToken cancellationToken) =>
        BfsAsync(workspaceId, entityId, SqlDependencyPredicates, SemanticRelationshipDirection.Incoming, maxDepth, cancellationToken);

    /// <summary>
    /// API Intelligence: "what code path is reachable from this endpoint?" - walks forward
    /// (Outgoing) from an ApiEndpoint entity across ENDPOINT_INVOKES_METHOD (to its resolved
    /// handler) and then CALLS/METHOD_DEPENDS_ON_TYPE (Code Intelligence's own call graph),
    /// up to <paramref name="maxDepth"/> hops, each hop backed by a real, evidence-cited
    /// relationship.
    /// </summary>
    public Task<IReadOnlyList<SemanticImpactEntityResponse>> GetReachableCodePathAsync(Guid workspaceId, Guid endpointEntityId, int maxDepth, CancellationToken cancellationToken) =>
        BfsAsync(workspaceId, endpointEntityId, CodeCallChainPredicates, SemanticRelationshipDirection.Outgoing, maxDepth, cancellationToken);

    /// <summary>
    /// API Intelligence: "which endpoints may be affected by a changed method?" - walks
    /// backward (Incoming) from a Method entity across the same predicate set as
    /// <see cref="GetReachableCodePathAsync"/>, then filters the closure down to
    /// ApiEndpoint entities (a method several calls deep can still be reached from more than
    /// one endpoint, each surfaced with the specific hop that led to it).
    /// </summary>
    public async Task<IReadOnlyList<SemanticImpactEntityResponse>> GetEndpointsAffectedByMethodAsync(Guid workspaceId, Guid methodEntityId, int maxDepth, CancellationToken cancellationToken)
    {
        var closure = await BfsAsync(workspaceId, methodEntityId, CodeCallChainPredicates, SemanticRelationshipDirection.Incoming, maxDepth, cancellationToken);
        return closure.Where(item => item.Entity.EntityType == "ApiEndpoint").ToList();
    }

    /// <summary>
    /// Generic multi-hop traversal shared by every "impact"/"reachability" query: starting
    /// from <paramref name="startEntityId"/>, repeatedly walks every relationship matching
    /// one of <paramref name="predicates"/> in <paramref name="direction"/>, breadth-first,
    /// up to <paramref name="maxDepth"/> hops. Never fabricates a hop - every entity returned
    /// was reached through <see cref="GetRelatedEntitiesAsync"/>, i.e. a real, current,
    /// evidence-backed SemanticRelationship row.
    /// </summary>
    private async Task<IReadOnlyList<SemanticImpactEntityResponse>> BfsAsync(
        Guid workspaceId,
        Guid startEntityId,
        IReadOnlyList<string> predicates,
        SemanticRelationshipDirection direction,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { startEntityId };
        var frontier = new List<Guid> { startEntityId };
        var results = new List<SemanticImpactEntityResponse>();
        var depth = 0;

        while (frontier.Count > 0 && depth < maxDepth)
        {
            depth++;
            var nextFrontier = new List<Guid>();

            foreach (var id in frontier)
            {
                foreach (var predicate in predicates)
                {
                    var related = await GetRelatedEntitiesAsync(workspaceId, id, predicate, direction, cancellationToken);
                    foreach (var relation in related)
                    {
                        if (!visited.Add(relation.Entity.EntityId))
                            continue;

                        results.Add(new SemanticImpactEntityResponse(
                            relation.Entity,
                            depth,
                            relation.RelationshipId,
                            relation.Predicate,
                            relation.EvidenceId,
                            relation.DocumentVersionId,
                            relation.SourceLocation));
                        nextFrontier.Add(relation.Entity.EntityId);
                    }
                }
            }

            frontier = nextFrontier;
        }

        return results
            .OrderBy(result => result.Depth)
            .ThenBy(result => result.Entity.CanonicalName, StringComparer.Ordinal)
            .ToList();
    }

    private static SemanticEntityResponse ToEntityResponse(SemanticEntity entity) =>
        new(entity.Id, entity.WorkspaceId, entity.EntityType, entity.CanonicalName, entity.Aliases);
}