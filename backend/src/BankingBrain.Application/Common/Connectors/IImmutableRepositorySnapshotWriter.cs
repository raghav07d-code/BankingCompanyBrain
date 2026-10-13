namespace BankingBrain.Application.Common.Connectors;

/// <summary>
/// Writes a Git knowledge source's artifact content - already fully read into memory by
/// <c>KnowledgeSourceSnapshotService.SnapshotAsync</c> before this is ever called, so there
/// is no possible read-after-the-fact race - to an immutable, revision-keyed directory that
/// is never mutated once written. Analyzers that need project-aware, multi-file access to
/// repository content (Code Intelligence, API Intelligence, Data Access Intelligence)
/// resolve their input from this directory instead of the connector's live working copy,
/// so a later commit changing or deleting a file cannot affect an extraction already bound
/// to an earlier revision.
/// </summary>
public interface IImmutableRepositorySnapshotWriter
{
    /// <returns>The absolute path of the revision's immutable directory.</returns>
    Task<string> WriteAsync(
        Guid knowledgeSourceId,
        SourceSnapshot snapshot,
        CancellationToken cancellationToken);
}
