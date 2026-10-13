using System.Diagnostics;
using BankingBrain.Application.Common.Connectors;
using BankingBrain.Domain.Entities;
using BankingBrain.Domain.Entities.KnowledgeSource;
using BankingBrain.Domain.Entities.Workspace;
using BankingBrain.Domain.Enums;
using BankingBrain.Application.Common.Connectors.Reconciliation;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class GitSnapshotReconciliationTests
    : IClassFixture<BankingBrainApiFactory>
{
    private readonly BankingBrainApiFactory _factory;

    public GitSnapshotReconciliationTests(BankingBrainApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reconciliation_ShouldCreateDocumentVersionAndJobForNewArtifact()
    {
        using var scope = _factory.Services.CreateScope();
        var (workspaceId, sourceId) = await CreateTestSourceAsync(scope);

        var snapshot = CreateSnapshot(
            workspaceId,
            sourceId,
            "Customer.cs",
            "public class Customer { }",
            "commit-1");

        var service = scope.ServiceProvider
            .GetRequiredService<GitSnapshotReconciliationService>();

        var result = await service.ReconcileAsync(
            snapshot,
            workspaceId,
            sourceId,
            CancellationToken.None);

        Assert.Equal(1, result.CreatedDocuments);
        Assert.Equal(1, result.CreatedVersions);
        Assert.Equal(0, result.UnchangedArtifacts);

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var document = await db.Documents
            .SingleAsync(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == sourceId);

        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .ToListAsync();

        var jobs = await db.ProcessingJobs
            .Where(x => versions.Select(v => v.Id).Contains(x.DocumentVersionId))
            .ToListAsync();

        Assert.Equal("Customer.cs", document.ExternalIdentifier);
        Assert.True(document.IsActive);
        Assert.Single(versions);
        Assert.Equal(1, versions[0].VersionNumber);
        Assert.Single(jobs);
    }

    [Fact]
    public async Task Reconciliation_ShouldNotCreateNewVersionForUnchangedContent()
    {
        using var scope = _factory.Services.CreateScope();
        var (workspaceId, sourceId) = await CreateTestSourceAsync(scope);

        var first = CreateSnapshot(
            workspaceId,
            sourceId,
            "Customer.cs",
            "public class Customer { }",
            "commit-1");

        var second = CreateSnapshot(
            workspaceId,
            sourceId,
            "Customer.cs",
            "public class Customer { }",
            "commit-2");

        var service = scope.ServiceProvider
            .GetRequiredService<GitSnapshotReconciliationService>();

        var firstResult = await service.ReconcileAsync(
            first,
            workspaceId,
            sourceId,
            CancellationToken.None);

        var secondResult = await service.ReconcileAsync(
            second,
            workspaceId,
            sourceId,
            CancellationToken.None);

        Assert.Equal(1, firstResult.CreatedVersions);
        Assert.Equal(1, secondResult.UnchangedArtifacts);
        Assert.Equal(0, secondResult.CreatedVersions);

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var document = await db.Documents
            .SingleAsync(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == sourceId);

        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .ToListAsync();

        Assert.Single(versions);
        Assert.Equal(1, versions[0].VersionNumber);
    }

    [Fact]
    public async Task Reconciliation_ShouldCreateNewVersionWhenContentChanges()
    {
        using var scope = _factory.Services.CreateScope();
        var (workspaceId, sourceId) = await CreateTestSourceAsync(scope);

        var service = scope.ServiceProvider
            .GetRequiredService<GitSnapshotReconciliationService>();

        await service.ReconcileAsync(
            CreateSnapshot(
                workspaceId,
                sourceId,
                "Customer.cs",
                "public class Customer { }",
                "commit-1"),
            workspaceId,
            sourceId,
            CancellationToken.None);

        var result = await service.ReconcileAsync(
            CreateSnapshot(
                workspaceId,
                sourceId,
                "Customer.cs",
                "public class Customer { public int Id { get; set; } }",
                "commit-2"),
            workspaceId,
            sourceId,
            CancellationToken.None);

        Assert.Equal(1, result.CreatedVersions);

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var document = await db.Documents
            .SingleAsync(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == sourceId);

        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync();

        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions[0].VersionNumber);
        Assert.Equal(2, versions[1].VersionNumber);
        Assert.NotEqual(
            versions[0].ContentHash,
            versions[1].ContentHash);

        Assert.Equal(
            versions[1].Id,
            document.CurrentVersionId);
    }

    [Fact]
    public async Task Reconciliation_ShouldDeactivateDeletedArtifact()
    {
        using var scope = _factory.Services.CreateScope();
        var (workspaceId, sourceId) = await CreateTestSourceAsync(scope);

        var service = scope.ServiceProvider
            .GetRequiredService<GitSnapshotReconciliationService>();

        await service.ReconcileAsync(
            CreateSnapshot(
                workspaceId,
                sourceId,
                "Customer.cs",
                "public class Customer { }",
                "commit-1"),
            workspaceId,
            sourceId,
            CancellationToken.None);

        var result = await service.ReconcileAsync(
            new SourceSnapshot(
                "git",
                "test-repository",
                "commit-2",
                DateTime.UtcNow,
                Array.Empty<SourceArtifact>()),
            workspaceId,
            sourceId,
            CancellationToken.None);

        Assert.Equal(1, result.DeactivatedDocuments);

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var document = await db.Documents
            .SingleAsync(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == sourceId);

        Assert.False(document.IsActive);

        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .ToListAsync();

        Assert.Single(versions);
    }

    [Fact]
    public async Task Reconciliation_ShouldReactivateDeletedArtifactWithoutCreatingVersion()
    {
        using var scope = _factory.Services.CreateScope();
        var (workspaceId, sourceId) = await CreateTestSourceAsync(scope);

        var service = scope.ServiceProvider
            .GetRequiredService<GitSnapshotReconciliationService>();

        var first = CreateSnapshot(
            workspaceId,
            sourceId,
            "Customer.cs",
            "public class Customer { }",
            "commit-1");

        await service.ReconcileAsync(
            first,
            workspaceId,
            sourceId,
            CancellationToken.None);

        await service.ReconcileAsync(
            new SourceSnapshot(
                "git",
                "test-repository",
                "commit-2",
                DateTime.UtcNow,
                Array.Empty<SourceArtifact>()),
            workspaceId,
            sourceId,
            CancellationToken.None);

        var result = await service.ReconcileAsync(
            CreateSnapshot(
                workspaceId,
                sourceId,
                "Customer.cs",
                "public class Customer { }",
                "commit-3"),
            workspaceId,
            sourceId,
            CancellationToken.None);

        Assert.Equal(1, result.ReactivatedDocuments);
        Assert.Equal(1, result.UnchangedArtifacts);
        Assert.Equal(0, result.CreatedVersions);

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var document = await db.Documents
            .SingleAsync(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == sourceId);

        Assert.True(document.IsActive);

        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .ToListAsync();

        Assert.Single(versions);
    }

    [Fact]
    public async Task Reconciliation_ShouldKeepDifferentSourcesIsolated()
    {
        using var scope = _factory.Services.CreateScope();

        var (workspaceId, sourceA) = await CreateTestSourceAsync(scope);

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var sourceBEntity = KnowledgeSource.Create(
            workspaceId,
            $"Git Test Source {Guid.NewGuid():N}",
            null,
            KnowledgeSourceType.Repository,
            "git",
            $"repository-{Guid.NewGuid():N}");

        db.KnowledgeSources.Add(sourceBEntity);
        await db.SaveChangesAsync();

        var sourceB = sourceBEntity.Id;

        var service = scope.ServiceProvider
            .GetRequiredService<GitSnapshotReconciliationService>();

        await service.ReconcileAsync(
            CreateSnapshot(
                workspaceId,
                sourceA,
                "Customer.cs",
                "source A",
                "commit-a"),
            workspaceId,
            sourceA,
            CancellationToken.None);

        await service.ReconcileAsync(
            CreateSnapshot(
                workspaceId,
                sourceB,
                "Customer.cs",
                "source B",
                "commit-b"),
            workspaceId,
            sourceB,
            CancellationToken.None);

        var documents = await db.Documents
            .Where(x =>
                x.WorkspaceId == workspaceId &&
                (x.KnowledgeSourceId == sourceA ||
                 x.KnowledgeSourceId == sourceB))
            .ToListAsync();

        Assert.Equal(2, documents.Count);
        Assert.Equal(2, documents.Select(x => x.KnowledgeSourceId).Distinct().Count());
    }

    [Fact]
    public async Task Reconciliation_ShouldKeepDifferentWorkspacesIsolated()
    {
        using var scope = _factory.Services.CreateScope();

        var (workspaceA, sourceA) = await CreateTestSourceAsync(scope);
        var (workspaceB, sourceB) = await CreateTestSourceAsync(scope);

        var service = scope.ServiceProvider
            .GetRequiredService<GitSnapshotReconciliationService>();

        await service.ReconcileAsync(
            CreateSnapshot(
                workspaceA,
                sourceA,
                "Customer.cs",
                "workspace A",
                "commit-a"),
            workspaceA,
            sourceA,
            CancellationToken.None);

        await service.ReconcileAsync(
            CreateSnapshot(
                workspaceB,
                sourceB,
                "Customer.cs",
                "workspace B",
                "commit-b"),
            workspaceB,
            sourceB,
            CancellationToken.None);

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var documents = await db.Documents
            .Where(x =>
                x.ExternalIdentifier == "Customer.cs" &&
                (x.WorkspaceId == workspaceA ||
                 x.WorkspaceId == workspaceB))
            .ToListAsync();

        Assert.Equal(2, documents.Count);
        Assert.Contains(documents, x => x.WorkspaceId == workspaceA);
        Assert.Contains(documents, x => x.WorkspaceId == workspaceB);
    }

    private static async Task<(Guid WorkspaceId, Guid SourceId)> CreateTestSourceAsync(
        IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = await db.Users
            .SingleAsync(x => x.Email == "admin@brain.ai");

        var workspace = Workspace.Create(
            admin.Id,
            $"Git Test Workspace {Guid.NewGuid():N}",
            null);

        var source = KnowledgeSource.Create(
            workspace.Id,
            $"Git Test Source {Guid.NewGuid():N}",
            null,
            KnowledgeSourceType.Repository,
            "git",
            $"repository-{Guid.NewGuid():N}");

        db.Workspaces.Add(workspace);
        db.KnowledgeSources.Add(source);

        await db.SaveChangesAsync();

        return (workspace.Id, source.Id);
    }

    private static SourceSnapshot CreateSnapshot(
        Guid workspaceId,
        Guid sourceId,
        string path,
        string content,
        string revision)
    {
        return new SourceSnapshot(
            "git",
            $"repository-{sourceId:N}",
            revision,
            DateTime.UtcNow,
            [
                new SourceArtifact(
                    path,
                    path,
                    SourceArtifactKind.SourceCode,
                    content,
                    "text/x-csharp",
                    path,
                    revision)
            ]);
    }
}
