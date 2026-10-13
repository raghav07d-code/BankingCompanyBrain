using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BankingBrain.Application.Common.Graph;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class SemanticGraphService : ISemanticGraphService
{
    private readonly ApplicationDbContext _db;

    public SemanticGraphService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Reprojects the workspace graph from current semantic state - but as a diff against
    /// what is already stored, not a wholesale delete-and-recreate: edges are matched by
    /// their deterministic IdentityHash, so an edge that is still desired is left completely
    /// untouched (no new row, no new ProjectedOnUtc), only edges that are no longer desired
    /// (superseded/deactivated code relationships) are removed and only newly-desired edges
    /// are added. Nodes are still one per SemanticEntity (entities are never deleted from the
    /// semantic model), added only when missing - never rewritten or removed.
    /// </summary>
    public async Task RebuildAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var entities = await _db.SemanticEntities.AsNoTracking().Where(entity => entity.WorkspaceId == workspaceId).ToListAsync(cancellationToken);

        var currentVersionIds = CurrentDocumentVersionFilter.ActiveCurrentVersionIds(_db, workspaceId);
        var relationships = await _db.SemanticRelationships.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && currentVersionIds.Contains(item.DocumentVersionId))
            .ToListAsync(cancellationToken);
        var claims = await _db.SemanticClaims.AsNoTracking().Where(item => item.WorkspaceId == workspaceId && item.SubjectEntityId != null && item.ObjectEntityId != null).ToListAsync(cancellationToken);
        var entityObjectPredicates = await _db.SemanticPredicates.AsNoTracking().Where(predicate => predicate.WorkspaceId == workspaceId && predicate.AllowsEntityObject).Select(predicate => predicate.Code).ToListAsync(cancellationToken);
        var validClaims = claims.Where(claim => entityObjectPredicates.Contains(claim.Predicate.ToUpperInvariant())).ToList();

        var claimLookup = validClaims.GroupBy(claim => (claim.SubjectEntityId!.Value, claim.ObjectEntityId!.Value, Predicate: claim.Predicate.ToUpperInvariant())).ToDictionary(group => group.Key, group => group.ToList());
        var desiredEdges = new List<SemanticGraphEdge>();
        foreach (var relationship in relationships)
        {
            var key = (relationship.SubjectEntityId, relationship.ObjectEntityId, relationship.RelationType.ToUpperInvariant());
            claimLookup.TryGetValue(key, out var supporting);
            supporting ??= new List<SemanticClaim>();
            desiredEdges.Add(CreateEdge(workspaceId, relationship.SubjectEntityId, relationship.ObjectEntityId, relationship.RelationType, relationship.Id, supporting, false));
        }
        foreach (var claim in validClaims)
            desiredEdges.Add(CreateEdge(workspaceId, claim.SubjectEntityId!.Value, claim.ObjectEntityId!.Value, claim.Predicate, null, new[] { claim }, true));

        var desiredByHash = desiredEdges
            .GroupBy(edge => edge.IdentityHash)
            .ToDictionary(group => group.Key, group => group.First());

        var existingEdges = await _db.SemanticGraphEdges.Where(edge => edge.WorkspaceId == workspaceId).ToListAsync(cancellationToken);
        var existingHashes = existingEdges.Select(edge => edge.IdentityHash).ToHashSet();

        _db.SemanticGraphEdges.RemoveRange(
            existingEdges.Where(edge => !desiredByHash.ContainsKey(edge.IdentityHash)));
        _db.SemanticGraphEdges.AddRange(
            desiredByHash.Where(pair => !existingHashes.Contains(pair.Key)).Select(pair => pair.Value));

        var existingNodeEntityIds = await _db.SemanticGraphNodes
            .Where(node => node.WorkspaceId == workspaceId)
            .Select(node => node.EntityId)
            .ToListAsync(cancellationToken);
        var existingNodeEntityIdSet = existingNodeEntityIds.ToHashSet();

        _db.SemanticGraphNodes.AddRange(
            entities
                .Where(entity => !existingNodeEntityIdSet.Contains(entity.Id))
                .Select(entity => SemanticGraphNode.Create(workspaceId, entity.Id)));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SemanticGraphResult> QueryAsync(Guid workspaceId, SemanticGraphQuery query, CancellationToken cancellationToken)
    {
        var edges = await _db.SemanticGraphEdges.AsNoTracking().Where(edge => edge.WorkspaceId == workspaceId).ToListAsync(cancellationToken);
        var filtered = edges.Where(edge => (query.Predicate is null || edge.Predicate == query.Predicate.Trim().ToUpperInvariant()) &&
            (query.AsOfUtc is null || edge.AsOfUtc is null || edge.AsOfUtc <= query.AsOfUtc) &&
            (query.ValidAtUtc is null || (edge.ValidFromUtc is null || edge.ValidFromUtc <= query.ValidAtUtc) && (edge.ValidToUtc is null || edge.ValidToUtc >= query.ValidAtUtc))).ToList();
        var selected = new HashSet<Guid>();
        if (query.SubjectEntityId is null)
            foreach (var edge in filtered) selected.Add(edge.Id);
        else
        {
            var frontier = new HashSet<Guid> { query.SubjectEntityId.Value };
            var depth = Math.Clamp(query.MaxDepth, 1, 10);
            for (var level = 0; level < depth && frontier.Count > 0; level++)
            {
                var next = new HashSet<Guid>();
                var isFinalDepth = level == depth - 1;
                foreach (var edge in filtered.Where(edge => frontier.Contains(edge.SubjectEntityId) && (!isFinalDepth || query.ObjectEntityId is null || edge.ObjectEntityId == query.ObjectEntityId))) { selected.Add(edge.Id); next.Add(edge.ObjectEntityId); }
                frontier = next;
            }
        }
        var resultEdges = filtered.Where(edge => selected.Contains(edge.Id)).OrderBy(edge => edge.SubjectEntityId).ThenBy(edge => edge.Predicate).ThenBy(edge => edge.ObjectEntityId).ToList();
        return new SemanticGraphResult(resultEdges.Select(ToModel).ToList(), resultEdges.SelectMany(edge => new[] { edge.SubjectEntityId, edge.ObjectEntityId }).Distinct().ToList());
    }

    private static SemanticGraphEdge CreateEdge(Guid workspaceId, Guid subject, Guid objectId, string predicate, Guid? relationshipId, IReadOnlyList<SemanticClaim> claims, bool derived)
    {
        var claimIds = claims.Select(claim => claim.Id).ToList();
        var evidenceIds = claims.Select(claim => claim.EvidenceId).Distinct().ToList();
        var first = claims.OrderByDescending(claim => claim.AsOfUtc ?? claim.CreatedOnUtc).FirstOrDefault();
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relationshipId is not null ? $"relationship|{relationshipId:N}" : $"claim|{claims.Single().Id:N}")));
        return SemanticGraphEdge.Create(workspaceId, subject, objectId, predicate, relationshipId, JsonSerializer.Serialize(claimIds), JsonSerializer.Serialize(evidenceIds), first?.DocumentVersionId, first?.ValidFromUtc, first?.ValidToUtc, first?.AsOfUtc, first?.ExtractionConfidence, first?.SourceAuthorityScore, derived, identity);
    }

    private static SemanticGraphEdgeModel ToModel(SemanticGraphEdge edge) => new(edge.SubjectEntityId, edge.ObjectEntityId, edge.Predicate, edge.SourceRelationshipId, JsonSerializer.Deserialize<List<Guid>>(edge.SupportingClaimIdsJson) ?? new(), JsonSerializer.Deserialize<List<Guid>>(edge.EvidenceIdsJson) ?? new(), edge.DocumentVersionId, edge.ValidFromUtc, edge.ValidToUtc, edge.AsOfUtc, edge.ExtractionConfidence, edge.SourceAuthorityScore, edge.IsDerived, edge.IdentityHash);
}