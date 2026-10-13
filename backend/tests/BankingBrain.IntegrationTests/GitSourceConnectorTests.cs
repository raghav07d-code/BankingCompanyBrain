using System.Diagnostics;
using BankingBrain.Application.Common.Connectors;
using BankingBrain.Infrastructure.Configuration.Connectors;
using BankingBrain.Infrastructure.Connectors;
using BankingBrain.Infrastructure.Connectors.Git;
using Microsoft.Extensions.Options;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class GitSourceConnectorTests
{
    [Fact]
    public async Task GitConnector_ShouldSnapshotTrackedFilesAndCaptureRevision()
    {
        var root = CreateTempDirectory();

        try
        {
            await RunGit(root, "init");
            await RunGit(root, "config", "user.email", "test@bankingbrain.local");
            await RunGit(root, "config", "user.name", "Banking Brain Test");

            await File.WriteAllTextAsync(
                Path.Combine(root, "Customer.cs"),
                "public class Customer { }");

            Directory.CreateDirectory(Path.Combine(root, "docs"));

            await File.WriteAllTextAsync(
                Path.Combine(root, "docs", "README.md"),
                "# Banking Brain");

            await File.WriteAllBytesAsync(
                Path.Combine(root, "image.bin"),
                [0x00, 0x01, 0x02]);

            await RunGit(root, "add", ".");
            await RunGit(root, "commit", "-m", "initial");

            var connector = CreateConnector(root);

            var snapshot = await connector.SnapshotAsync(
                new SourceConnectorRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "git",
                    root),
                CancellationToken.None);

            Assert.Equal("git", snapshot.ConnectorKey);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Revision));
            Assert.Equal(3, snapshot.Artifacts.Count);

            var source = snapshot.Artifacts.Single(
                x => x.ExternalIdentifier == "Customer.cs");

            Assert.Equal(SourceArtifactKind.SourceCode, source.Kind);
            Assert.Equal(
                "public class Customer { }",
                source.Content);
            Assert.Equal(
                "text/x-csharp",
                source.MediaType);
            Assert.Equal(
                snapshot.Revision,
                source.Revision);

            var readme = snapshot.Artifacts.Single(
                x => x.ExternalIdentifier == "docs/README.md");

            Assert.Equal(
                "# Banking Brain",
                readme.Content);

            var binary = snapshot.Artifacts.Single(
                x => x.ExternalIdentifier == "image.bin");

            Assert.Null(binary.Content);
            Assert.Equal(
                "application/octet-stream",
                binary.MediaType);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GitConnector_ShouldRejectNonGitDirectory()
    {
        var root = CreateTempDirectory();

        try
        {
            var connector = CreateConnector(root);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => connector.SnapshotAsync(
                    new SourceConnectorRequest(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "git",
                        root),
                    CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GitConnector_ShouldRejectMissingRepository()
    {
        var root = CreateTempDirectory();
        var missing = Path.Combine(root, "missing");

        try
        {
            var connector = CreateConnector(root);

            await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => connector.SnapshotAsync(
                    new SourceConnectorRequest(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "git",
                        missing),
                    CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GitConnector_ShouldRejectPathOutsideAllowedRoot()
    {
        var allowedRoot = CreateTempDirectory();
        var outsideRoot = CreateTempDirectory();

        try
        {
            await RunGit(outsideRoot, "init");
            await RunGit(outsideRoot, "config", "user.email", "test@bankingbrain.local");
            await RunGit(outsideRoot, "config", "user.name", "Banking Brain Test");

            var connector = CreateConnector(allowedRoot);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => connector.SnapshotAsync(
                    new SourceConnectorRequest(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "git",
                        outsideRoot),
                    CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(allowedRoot);
            DeleteDirectory(outsideRoot);
        }
    }

    [Fact]
    public async Task GitConnector_ShouldRespectMaxFiles()
    {
        var root = CreateTempDirectory();

        try
        {
            await RunGit(root, "init");
            await RunGit(root, "config", "user.email", "test@bankingbrain.local");
            await RunGit(root, "config", "user.name", "Banking Brain Test");

            for (var i = 0; i < 3; i++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(root, $"file-{i}.txt"),
                    $"file {i}");
            }

            await RunGit(root, "add", ".");
            await RunGit(root, "commit", "-m", "files");

            var connector = CreateConnector(
                root,
                maxFiles: 2);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => connector.SnapshotAsync(
                    new SourceConnectorRequest(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "git",
                        root),
                    CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static GitSourceConnector CreateConnector(
        string root,
        int maxFiles = 10000,
        long maxTextFileBytes = 5 * 1024 * 1024)
    {
        var options = Options.Create(
            new ConnectorOptions
            {
                Git = new GitConnectorOptions
                {
                    AllowedRoots = [root],
                    GitExecutable = "git",
                    CommandTimeoutSeconds = 30,
                    MaxFiles = maxFiles,
                    MaxTextFileBytes = maxTextFileBytes
                }
            });

        return new GitSourceConnector(
            new ConnectorPathPolicy(options),
            options);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"banking-brain-git-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static async Task<string> RunGit(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Git failed: {error}");

        return output;
    }
}
