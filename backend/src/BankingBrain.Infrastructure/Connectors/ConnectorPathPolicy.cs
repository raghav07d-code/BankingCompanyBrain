using BankingBrain.Infrastructure.Configuration.Connectors;
using Microsoft.Extensions.Options;

namespace BankingBrain.Infrastructure.Connectors;

public sealed class ConnectorPathPolicy
{
    private readonly ConnectorOptions _options;

    public ConnectorPathPolicy(IOptions<ConnectorOptions> options)
    {
        _options = options.Value;
    }

    public string ResolveFileSystemPath(string path)
    {
        return ResolveUnderAllowedRoots(
            path,
            _options.FileSystem.AllowedRoots,
            "filesystem");
    }

    public string ResolveGitRepositoryPath(string path)
    {
        return ResolveUnderAllowedRoots(
            path,
            _options.Git.AllowedRoots,
            "git");
    }

    /// <summary>
    /// Resolves the immutable, revision-keyed directory a Git knowledge source's content
    /// was written to by <c>IImmutableRepositorySnapshotWriter</c> for a specific revision -
    /// never the connector's live, mutable working copy. Distinct revisions get distinct
    /// directories; the same revision always resolves to the same path, by construction.
    /// </summary>
    public string ResolveGitSnapshotRoot(Guid knowledgeSourceId, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("Revision is required.", nameof(revision));

        var snapshotRoot = string.IsNullOrWhiteSpace(_options.Git.SnapshotRootPath)
            ? "repository-snapshots"
            : _options.Git.SnapshotRootPath;

        return Path.GetFullPath(
            Path.Combine(
                snapshotRoot,
                knowledgeSourceId.ToString("N"),
                SanitizeRevision(revision)));
    }

    private static string SanitizeRevision(string revision)
    {
        var sanitized = new string(revision.Trim().Where(char.IsLetterOrDigit).ToArray());

        if (sanitized.Length == 0)
            throw new ArgumentException("Revision must contain at least one alphanumeric character.", nameof(revision));

        return sanitized;
    }

    private static string ResolveUnderAllowedRoots(
        string path,
        IReadOnlyCollection<string> allowedRoots,
        string connectorName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(
                "Source path is required.",
                nameof(path));

        if (allowedRoots.Count == 0)
            throw new InvalidOperationException(
                $"No allowed roots are configured for the {connectorName} connector.");

        var fullPath = Path.GetFullPath(path);

        foreach (var configuredRoot in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
                continue;

            var root = Path.GetFullPath(configuredRoot);

            if (!Directory.Exists(root))
                continue;

            var relative = Path.GetRelativePath(root, fullPath);

            if (relative == "."
                || (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && relative != ".."
                    && !Path.IsPathRooted(relative)))
            {
                return fullPath;
            }
        }

        throw new UnauthorizedAccessException(
            $"Source path '{fullPath}' is outside the configured {connectorName} connector roots.");
    }
}
