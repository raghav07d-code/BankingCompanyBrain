using BankingBrain.Application.Common.Connectors;

using BankingBrain.Infrastructure.Connectors;

namespace BankingBrain.Infrastructure.Connectors.FileSystem;

public sealed class FileSystemSourceConnector : ISourceConnector
{
    private readonly ConnectorPathPolicy _pathPolicy;

    public FileSystemSourceConnector(ConnectorPathPolicy pathPolicy)
    {
        _pathPolicy = pathPolicy;
    }

    public string ConnectorKey => "filesystem";

    public async Task<SourceSnapshot> SnapshotAsync(
        SourceConnectorRequest request,
        CancellationToken cancellationToken)
    {
        if (request.WorkspaceId == Guid.Empty)
            throw new ArgumentException(
                "Workspace ID is required.",
                nameof(request));

        if (request.KnowledgeSourceId == Guid.Empty)
            throw new ArgumentException(
                "Knowledge source ID is required.",
                nameof(request));

        if (string.IsNullOrWhiteSpace(request.ExternalIdentifier))
            throw new ArgumentException(
                "Filesystem path is required.",
                nameof(request));

        var rootPath = _pathPolicy.ResolveFileSystemPath(
            request.ExternalIdentifier);

        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(
                $"Filesystem source '{rootPath}' was not found.");

        var artifacts = new List<SourceArtifact>();

        foreach (var filePath in Directory.EnumerateFiles(
                     rootPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(
                rootPath,
                filePath);

            var content = await ReadTextContentAsync(
                filePath,
                cancellationToken);

            var externalIdentifier = NormalizePath(relativePath);

            artifacts.Add(
                new SourceArtifact(
                    externalIdentifier,
                    Path.GetFileName(filePath),
                    SourceArtifactKind.File,
                    content,
                    GetMediaType(filePath),
                    externalIdentifier,
                    null));
        }

        return new SourceSnapshot(
            ConnectorKey,
            request.ExternalIdentifier,
            request.Revision,
            DateTime.UtcNow,
            artifacts);
    }

    private static async Task<string?> ReadTextContentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath);

        if (!IsTextFile(extension))
            return null;

        return await File.ReadAllTextAsync(
            filePath,
            cancellationToken);
    }

    private static bool IsTextFile(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".txt" => true,
            ".md" => true,
            ".json" => true,
            ".xml" => true,
            ".csv" => true,
            ".html" => true,
            ".htm" => true,
            ".cs" => true,
            ".js" => true,
            ".ts" => true,
            ".tsx" => true,
            ".jsx" => true,
            ".py" => true,
            ".java" => true,
            ".sql" => true,
            ".yml" => true,
            ".yaml" => true,
            ".config" => true,
            ".csproj" => true,
            ".sln" => true,
            ".props" => true,
            ".targets" => true,
            _ => false
        };
    }

    private static string GetMediaType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            ".cs" => "text/x-csharp",
            ".py" => "text/x-python",
            ".js" => "text/javascript",
            ".ts" => "text/typescript",
            ".sql" => "application/sql",
            _ => "application/octet-stream"
        };
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(
            Path.DirectorySeparatorChar,
            '/');
    }
}
