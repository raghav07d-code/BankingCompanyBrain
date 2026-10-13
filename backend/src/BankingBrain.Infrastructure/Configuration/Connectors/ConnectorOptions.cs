namespace BankingBrain.Infrastructure.Configuration.Connectors;

public sealed class ConnectorOptions
{
    public const string SectionName = "Connectors";

    public FileSystemConnectorOptions FileSystem { get; set; } = new();
    public GitConnectorOptions Git { get; set; } = new();
}

public sealed class FileSystemConnectorOptions
{
    public List<string> AllowedRoots { get; set; } = [];
}

public sealed class GitConnectorOptions
{
    public List<string> AllowedRoots { get; set; } = [];
    public string GitExecutable { get; set; } = "git";
    public int CommandTimeoutSeconds { get; set; } = 120;
    public int MaxFiles { get; set; } = 10000;
    public long MaxTextFileBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Root directory for immutable, revision-keyed repository content snapshots (see <c>ConnectorPathPolicy.ResolveGitSnapshotRoot</c>) - distinct from <see cref="AllowedRoots"/>, which is the connector's own live, mutable working copy.</summary>
    public string SnapshotRootPath { get; set; } = "repository-snapshots";
}
