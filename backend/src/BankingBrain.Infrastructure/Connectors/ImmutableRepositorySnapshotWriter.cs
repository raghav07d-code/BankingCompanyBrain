using System.Text;
using BankingBrain.Application.Common.Connectors;

namespace BankingBrain.Infrastructure.Connectors;

/// <summary>
/// Writes each artifact's already-in-memory content to <c>{dir}.tmp-{guid}</c> and only
/// then atomically renames it to the final <c>{dir}</c> path (a same-filesystem directory
/// move), so a reader can only ever see the directory once it is fully populated - never a
/// partially-written one. If the final directory already exists (this exact revision was
/// snapshotted before, or a concurrent writer won the race), the write is skipped/discarded:
/// a revision's content is immutable by construction, so re-writing it would be redundant
/// at best and could never legitimately differ.
/// </summary>
public sealed class ImmutableRepositorySnapshotWriter : IImmutableRepositorySnapshotWriter
{
    private readonly ConnectorPathPolicy _pathPolicy;

    public ImmutableRepositorySnapshotWriter(ConnectorPathPolicy pathPolicy)
    {
        _pathPolicy = pathPolicy;
    }

    public async Task<string> WriteAsync(
        Guid knowledgeSourceId,
        SourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Revision))
            throw new InvalidOperationException("Snapshot revision is required to write an immutable repository snapshot.");

        var finalDirectory = _pathPolicy.ResolveGitSnapshotRoot(knowledgeSourceId, snapshot.Revision);

        if (Directory.Exists(finalDirectory))
            return finalDirectory;

        var tempDirectory = $"{finalDirectory}.tmp-{Guid.NewGuid():N}";
        Directory.CreateDirectory(tempDirectory);

        try
        {
            foreach (var artifact in snapshot.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (artifact.Content is null || string.IsNullOrWhiteSpace(artifact.ExternalIdentifier))
                    continue;

                var relativePath = artifact.ExternalIdentifier.Replace('\\', '/').TrimStart('/');
                var destinationPath = Path.GetFullPath(Path.Combine(tempDirectory, relativePath));

                // Defensive: an artifact identifier must stay within the snapshot
                // directory. Never expected from a well-behaved connector, but this is
                // writing to disk from externally-sourced path strings.
                if (!destinationPath.StartsWith(Path.GetFullPath(tempDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await File.WriteAllTextAsync(destinationPath, artifact.Content, Encoding.UTF8, cancellationToken);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);

            try
            {
                Directory.Move(tempDirectory, finalDirectory);
            }
            catch (IOException) when (Directory.Exists(finalDirectory))
            {
                // A concurrent writer for the same revision won the rename race; its
                // content is identical by construction (same revision), so discard ours.
                Directory.Delete(tempDirectory, recursive: true);
            }

            return finalDirectory;
        }
        catch
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
            throw;
        }
    }
}
