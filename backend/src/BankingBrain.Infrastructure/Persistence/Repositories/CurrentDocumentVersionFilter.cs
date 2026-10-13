using BankingBrain.Infrastructure.Persistence.Context;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

/// <summary>
/// SemanticRelationship rows are never deleted or marked superseded (historical
/// provenance is preserved for future temporal queries), so "current" code state
/// is derived on read by checking whether a relationship's DocumentVersionId is
/// still the owning Document's CurrentVersionId and that Document is still active.
/// </summary>
internal static class CurrentDocumentVersionFilter
{
    public static IQueryable<Guid> ActiveCurrentVersionIds(ApplicationDbContext db, Guid workspaceId)
    {
        return db.Documents
            .Where(document =>
                document.WorkspaceId == workspaceId &&
                document.IsActive &&
                document.CurrentVersionId != null)
            .Select(document => document.CurrentVersionId!.Value);
    }

    /// <summary>
    /// A code entity has no per-version currency field of its own (unlike a relationship,
    /// which carries the DocumentVersionId it was derived from), so it is current exactly
    /// when it is still reachable as the subject or object of at least one current code
    /// relationship. An entity with none - e.g. a type whose only file was deleted/changed
    /// away - is orphaned: still persisted (never deleted), just no longer "current."
    /// </summary>
    public static IQueryable<Guid> CurrentEntityIds(ApplicationDbContext db, Guid workspaceId)
    {
        var currentVersionIds = ActiveCurrentVersionIds(db, workspaceId);

        var currentRelationships = db.SemanticRelationships
            .Where(relationship =>
                relationship.WorkspaceId == workspaceId &&
                currentVersionIds.Contains(relationship.DocumentVersionId));

        return currentRelationships
            .Select(relationship => relationship.SubjectEntityId)
            .Concat(currentRelationships.Select(relationship => relationship.ObjectEntityId))
            .Distinct();
    }
}
