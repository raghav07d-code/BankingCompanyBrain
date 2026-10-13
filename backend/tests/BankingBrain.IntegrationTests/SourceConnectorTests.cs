using Xunit;
using BankingBrain.Application.Common.Connectors;
using BankingBrain.Infrastructure.Connectors.FileSystem;
using BankingBrain.Infrastructure.Configuration.Connectors;
using Microsoft.Extensions.Options;

namespace BankingBrain.IntegrationTests;

public sealed class SourceConnectorTests
{

    private static FileSystemSourceConnector CreateFileSystemConnector(string root)
    {
        var options = Options.Create(new ConnectorOptions
        {
            FileSystem = new FileSystemConnectorOptions
            {
                AllowedRoots = [root]
            }
        });

        return new FileSystemSourceConnector(
            new BankingBrain.Infrastructure.Connectors.ConnectorPathPolicy(options));
    }

    [Fact]
    public async Task FileSystemConnector_ShouldSnapshotTextFiles()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"banking-brain-connector-{Guid.NewGuid():N}");

        Directory.CreateDirectory(root);

        try
        {
            var sourceFile = Path.Combine(root, "sample.cs");

            await File.WriteAllTextAsync(
                sourceFile,
                "public class Customer { }");

            var connector = CreateFileSystemConnector(root);

            var request = new SourceConnectorRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "filesystem",
                root);

            var snapshot = await connector.SnapshotAsync(
                request,
                CancellationToken.None);

            Assert.Equal("filesystem", snapshot.ConnectorKey);
            Assert.Single(snapshot.Artifacts);

            var artifact = snapshot.Artifacts.Single();

            Assert.Equal(
                SourceArtifactKind.File,
                artifact.Kind);

            Assert.Equal(
                "sample.cs",
                artifact.ExternalIdentifier);

            Assert.Equal(
                "public class Customer { }",
                artifact.Content);

            Assert.Equal(
                "text/x-csharp",
                artifact.MediaType);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemConnector_ShouldIgnoreBinaryContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"banking-brain-connector-{Guid.NewGuid():N}");

        Directory.CreateDirectory(root);

        try
        {
            var binaryFile = Path.Combine(root, "sample.bin");

            await File.WriteAllBytesAsync(
                binaryFile,
                [0x00, 0x01, 0x02, 0x03]);

            var connector = CreateFileSystemConnector(root);

            var request = new SourceConnectorRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "filesystem",
                root);

            var snapshot = await connector.SnapshotAsync(
                request,
                CancellationToken.None);

            var artifact = snapshot.Artifacts.Single();

            Assert.Equal(
                SourceArtifactKind.File,
                artifact.Kind);

            Assert.Null(artifact.Content);
            Assert.Equal(
                "application/octet-stream",
                artifact.MediaType);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemConnector_ShouldPreserveRelativePaths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"banking-brain-connector-{Guid.NewGuid():N}");

        var nestedDirectory = Path.Combine(
            root,
            "src",
            "domain");

        Directory.CreateDirectory(nestedDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(nestedDirectory, "Customer.cs"),
                "class Customer { }");

            var connector = CreateFileSystemConnector(root);

            var request = new SourceConnectorRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "filesystem",
                root);

            var snapshot = await connector.SnapshotAsync(
                request,
                CancellationToken.None);

            var artifact = snapshot.Artifacts.Single();

            Assert.Equal(
                "src/domain/Customer.cs",
                artifact.ExternalIdentifier);

            Assert.Equal(
                "src/domain/Customer.cs",
                artifact.Path);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemConnector_ShouldRejectMissingDirectory()
    {
        var allowedRoot = Path.Combine(
            Path.GetTempPath(),
            $"banking-brain-allowed-{Guid.NewGuid():N}");

        Directory.CreateDirectory(allowedRoot);

        try
        {
            var connector = CreateFileSystemConnector(allowedRoot);

            var missingDirectory = Path.Combine(
                allowedRoot,
                "missing");

            var request = new SourceConnectorRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "filesystem",
                missingDirectory);

            await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => connector.SnapshotAsync(
                    request,
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(allowedRoot))
                Directory.Delete(allowedRoot, recursive: true);
        }
    }

    [Fact]
    public void FileSystemConnector_ShouldExposeStableConnectorKey()
    {
        var allowedRoot = Path.Combine(
            Path.GetTempPath(),
            $"banking-brain-allowed-{Guid.NewGuid():N}");

        Directory.CreateDirectory(allowedRoot);

        try
        {
            var connector = CreateFileSystemConnector(allowedRoot);

            Assert.Equal(
                "filesystem",
                connector.ConnectorKey);
        }
        finally
        {
            if (Directory.Exists(allowedRoot))
                Directory.Delete(allowedRoot, recursive: true);
        }
    }
}

public sealed class SourceConnectorRegistryTests
{
    private static FileSystemSourceConnector CreateConnector()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"banking-brain-registry-{Guid.NewGuid():N}");

        Directory.CreateDirectory(root);

        return new FileSystemSourceConnector(
            new BankingBrain.Infrastructure.Connectors.ConnectorPathPolicy(
                Options.Create(new ConnectorOptions
                {
                    FileSystem = new FileSystemConnectorOptions
                    {
                        AllowedRoots = [root]
                    }
                })));
    }

        [Fact]
        public void Registry_ShouldResolveConnectorByKey()
        {
            var connector = CreateConnector();

            var registry = new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
                new ISourceConnector[] { connector });

            var resolved = registry.Get("filesystem");

            Assert.Same(connector, resolved);
        }

        [Fact]
        public void Registry_ShouldResolveConnectorCaseInsensitively()
        {
            var connector = CreateConnector();

            var registry = new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
                new ISourceConnector[] { connector });

            var resolved = registry.Get("FileSystem");

            Assert.Same(connector, resolved);
        }

        [Fact]
        public void Registry_ShouldReturnFalseForUnknownConnector()
        {
            var connector = CreateConnector();

            var registry = new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
                new ISourceConnector[] { connector });

            var found = registry.TryGet(
                "git",
                out var resolved);

            Assert.False(found);
            Assert.Null(resolved);
        }

        [Fact]
        public void Registry_ShouldRejectDuplicateConnectorKeys()
        {
            var first = CreateConnector();
            var second = CreateConnector();

            Assert.Throws<InvalidOperationException>(
                () => new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
                    new ISourceConnector[] { first, second }));
        }
    }

public sealed class KnowledgeSourceSnapshotServiceTests
{
    [Fact]
    public async Task SnapshotService_ShouldResolveSourceAndExecuteConnector()
    {
        var workspaceId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        var source = BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource.Create(
            workspaceId,
            "Test Files",
            null,
            BankingBrain.Domain.Enums.KnowledgeSourceType.File,
            "filesystem",
            "/tmp/test");

        var connector = new FakeSourceConnector(
            "filesystem");

        var repository = new FakeKnowledgeSourceRepository(source);

        var registry = new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
            new ISourceConnector[] { connector });

        var service = new BankingBrain.Application.Common.Connectors.KnowledgeSourceSnapshotService(
            new FakeCurrentUser(),
            new FakeWorkspaceRepository(),
            repository,
            registry);

        var snapshot = await service.SnapshotAsync(
            workspaceId,
            source.Id,
            null,
            CancellationToken.None);

        Assert.Equal("filesystem", snapshot.ConnectorKey);
        Assert.Equal("/tmp/test", snapshot.ExternalIdentifier);
        Assert.True(connector.WasCalled);
    }

    [Fact]
    public async Task SnapshotService_ShouldRejectMissingSource()
    {
        var repository = new FakeKnowledgeSourceRepository(null);

        var registry = new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
            Array.Empty<ISourceConnector>());

        var service = new BankingBrain.Application.Common.Connectors.KnowledgeSourceSnapshotService(
            new FakeCurrentUser(),
            new FakeWorkspaceRepository(),
            repository,
            registry);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.SnapshotAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task SnapshotService_ShouldRejectSourceWithoutConnectorKey()
    {
        var workspaceId = Guid.NewGuid();

        var source = BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource.Create(
            workspaceId,
            "Test Files",
            null,
            BankingBrain.Domain.Enums.KnowledgeSourceType.File,
            null,
            "/tmp/test");

        var repository = new FakeKnowledgeSourceRepository(source);

        var registry = new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
            Array.Empty<ISourceConnector>());

        var service = new BankingBrain.Application.Common.Connectors.KnowledgeSourceSnapshotService(
            new FakeCurrentUser(),
            new FakeWorkspaceRepository(),
            repository,
            registry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SnapshotAsync(
                workspaceId,
                source.Id,
                null,
                CancellationToken.None));
    }

    [Fact]
public async Task SnapshotService_ShouldRejectUnknownConnector()
{
    var workspaceId = Guid.NewGuid();

    var source = BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource.Create(
        workspaceId,
        "Test Files",
        null,
        BankingBrain.Domain.Enums.KnowledgeSourceType.File,
        "unknown-connector",
        "/tmp/test");

    var repository = new FakeKnowledgeSourceRepository(source);

    var registry = new BankingBrain.Infrastructure.Connectors.SourceConnectorRegistry(
        Array.Empty<ISourceConnector>());

    var service = new BankingBrain.Application.Common.Connectors.KnowledgeSourceSnapshotService(
        new FakeCurrentUser(),
        new FakeWorkspaceRepository(),
        repository,
        registry);

    await Assert.ThrowsAsync<KeyNotFoundException>(
        () => service.SnapshotAsync(
            workspaceId,
            source.Id,
            null,
            CancellationToken.None));
}
    private sealed class FakeCurrentUser : BankingBrain.Application.Common.Interfaces.ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Email => "test@test.local";
        public string FullName => "Test User";
        public bool IsAuthenticated => true;
    }

    private sealed class FakeWorkspaceRepository
        : BankingBrain.Application.Features.Workspaces.Interfaces.IWorkspaceRepository
    {
        public Task<bool> ExistsAsync(
            Guid ownerUserId,
            string name,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(
            BankingBrain.Domain.Entities.Workspace.Workspace workspace,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> SaveChangesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<IReadOnlyList<BankingBrain.Domain.Entities.Workspace.Workspace>> GetByOwnerAsync(
            Guid ownerUserId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BankingBrain.Domain.Entities.Workspace.Workspace>>(
                Array.Empty<BankingBrain.Domain.Entities.Workspace.Workspace>());

        public Task<BankingBrain.Domain.Entities.Workspace.Workspace?> GetByIdAsync(
            Guid ownerUserId,
            Guid workspaceId,
            CancellationToken cancellationToken)
            => Task.FromResult<BankingBrain.Domain.Entities.Workspace.Workspace?>(
                BankingBrain.Domain.Entities.Workspace.Workspace.Create(
                    ownerUserId,
                    "Test Workspace",
                    null));
    }

    private sealed class FakeSourceConnector : ISourceConnector
    {
        public string ConnectorKey { get; }

        public bool WasCalled { get; private set; }

        public FakeSourceConnector(string connectorKey)
        {
            ConnectorKey = connectorKey;
        }

        public Task<SourceSnapshot> SnapshotAsync(
            SourceConnectorRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(
                new SourceSnapshot(
                    ConnectorKey,
                    request.ExternalIdentifier,
                    request.Revision,
                    DateTime.UtcNow,
                    Array.Empty<SourceArtifact>()));
        }
    }

    private sealed class FakeKnowledgeSourceRepository
        : BankingBrain.Application.Features.KnowledgeSources.Interfaces.IKnowledgeSourceRepository
    {
        private readonly BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource? _source;

        public FakeKnowledgeSourceRepository(
            BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource? source)
        {
            _source = source;
        }

        public Task<bool> ExistsAsync(
            Guid workspaceId,
            string name,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(
            BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource source,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> SaveChangesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<IReadOnlyList<BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource>>
            GetByWorkspaceAsync(
                Guid workspaceId,
                CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource>>(
                Array.Empty<BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource>());

        public Task<BankingBrain.Domain.Entities.KnowledgeSource.KnowledgeSource?> GetByIdAsync(
            Guid workspaceId,
            Guid sourceId,
            CancellationToken cancellationToken)
            => Task.FromResult(
                _source is not null &&
                _source.WorkspaceId == workspaceId &&
                _source.Id == sourceId
                    ? _source
                    : null);

        public Task SetLastSnapshotRevisionAsync(
            Guid workspaceId,
            Guid knowledgeSourceId,
            string revision,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
