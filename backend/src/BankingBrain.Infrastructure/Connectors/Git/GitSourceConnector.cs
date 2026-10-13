using System.Diagnostics;
using BankingBrain.Application.Common.Connectors;
using BankingBrain.Infrastructure.Configuration.Connectors;
using Microsoft.Extensions.Options;

namespace BankingBrain.Infrastructure.Connectors.Git;

public sealed class GitSourceConnector : ISourceConnector
{
    private readonly ConnectorPathPolicy _pathPolicy;
    private readonly GitConnectorOptions _options;

    public GitSourceConnector(
        ConnectorPathPolicy pathPolicy,
        IOptions<ConnectorOptions> options)
    {
        _pathPolicy = pathPolicy;
        _options = options.Value.Git;
    }

    public string ConnectorKey => "git";

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
                "Git repository path is required.",
                nameof(request));

        var repositoryPath = _pathPolicy.ResolveGitRepositoryPath(
            request.ExternalIdentifier);

        if (!Directory.Exists(repositoryPath))
            throw new DirectoryNotFoundException(
                $"Git repository '{repositoryPath}' was not found.");

        var gitDirectory = Path.Combine(repositoryPath, ".git");

        if (!Directory.Exists(gitDirectory) && !File.Exists(gitDirectory))
            throw new InvalidOperationException(
                $"Path '{repositoryPath}' is not a Git repository.");

        var revision = await RunGitAsync(
            repositoryPath,
            ["rev-parse", "HEAD"],
            cancellationToken);

        var filesOutput = await RunGitAsync(
            repositoryPath,
            ["ls-files", "-z"],
            cancellationToken);

        var artifacts = new List<SourceArtifact>();

        foreach (var relativePath in filesOutput.Split(
                     '\0',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (artifacts.Count >= _options.MaxFiles)
                throw new InvalidOperationException(
                    $"Git repository exceeds the configured maximum of {_options.MaxFiles} files.");

            var normalizedPath = NormalizePath(relativePath);
            var fullPath = Path.GetFullPath(
                Path.Combine(repositoryPath, relativePath));

            var relativeCheck = Path.GetRelativePath(
                repositoryPath,
                fullPath);

            if (relativeCheck == ".."
                || relativeCheck.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                || Path.IsPathRooted(relativeCheck))
            {
                throw new InvalidOperationException(
                    $"Git file '{relativePath}' escapes the repository root.");
            }

            if (!File.Exists(fullPath))
                continue;

            var fileInfo = new FileInfo(fullPath);

            string? content = null;

            if (fileInfo.Length <= _options.MaxTextFileBytes
                && IsTextFile(fullPath))
            {
                content = await File.ReadAllTextAsync(
                    fullPath,
                    cancellationToken);
            }

            artifacts.Add(
                new SourceArtifact(
                    normalizedPath,
                    normalizedPath,
                    SourceArtifactKind.SourceCode,
                    content,
                    GetMediaType(fullPath),
                    normalizedPath,
                    revision));
        }

        return new SourceSnapshot(
            ConnectorKey,
            request.ExternalIdentifier,
            revision,
            DateTime.UtcNow,
            artifacts);
    }

    private async Task<string> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.GitExecutable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
            throw new InvalidOperationException(
                "Failed to start Git.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeoutCts.CancelAfter(
            TimeSpan.FromSeconds(_options.CommandTimeoutSeconds));

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(
                timeoutCts.Token);

            var errorTask = process.StandardError.ReadToEndAsync(
                timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Git command failed with exit code {process.ExitCode}: {error.Trim()}");

            return output;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Preserve the timeout exception.
            }

            throw new TimeoutException(
                $"Git command exceeded the configured timeout of {_options.CommandTimeoutSeconds} seconds.");
        }
    }

    private static bool IsTextFile(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
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
            ".sh" => true,
            ".bash" => true,
            ".dockerfile" => true,
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
        => path.Replace('\\', '/');
}
