using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BankingBrain.Domain.Entities;
using BankingBrain.Domain.Entities.DocumentVersion;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Domain.Enums;
using BankingBrain.Infrastructure.Identity.Passwords;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class KnowledgeDocumentTests
    : IClassFixture<BankingBrainApiFactory>
{
    private readonly BankingBrainApiFactory _factory;

    public KnowledgeDocumentTests(BankingBrainApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Owned_workspace_can_create_and_list_knowledge_sources()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources",
            new
            {
                name = $"source-{Guid.NewGuid():N}",
                description = "Banking source",
                sourceType = 1
            });

        var source = await createResponse.Content
            .ReadFromJsonAsync<KnowledgeSourceResponse>();
        var listResponse = await client.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources");
        var sources = await listResponse.Content
            .ReadFromJsonAsync<List<KnowledgeSourceResponse>>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(sources!, item => item.KnowledgeSourceId == source!.KnowledgeSourceId);
    }

    [Fact]
    public async Task Owned_workspace_can_snapshot_filesystem_knowledge_source()
{
    using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
    var workspace = await CreateWorkspaceAsync(client);

    var rootPath = Path.Combine(
        _factory.ConnectorSourcesRoot,
        $"BankingBrainSnapshot_{Guid.NewGuid():N}");

    Directory.CreateDirectory(rootPath);

    try
    {
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "accounts.txt"),
            "Account 1001 belongs to customer Alice.");

        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "notes.md"),
            "# Banking Notes\nCustomer account review.");

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources",
            new
            {
                name = $"filesystem-{Guid.NewGuid():N}",
                description = "Filesystem connector test",
                sourceType = 1,
                connectorKey = "filesystem",
                externalIdentifier = rootPath
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var source = await createResponse.Content
            .ReadFromJsonAsync<KnowledgeSourceResponse>();

        Assert.NotNull(source);

        var snapshotResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources/{source!.KnowledgeSourceId}/snapshot",
            new
            {
                revision = "test-revision-1"
            });

        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

        using var snapshotDocument =
            await snapshotResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.NotNull(snapshotDocument);

        var root = snapshotDocument!.RootElement;

        Assert.Equal(
            "filesystem",
            root.GetProperty("connectorKey").GetString());

        Assert.Equal(
            rootPath,
            root.GetProperty("externalIdentifier").GetString());

        Assert.Equal(
            "test-revision-1",
            root.GetProperty("revision").GetString());

        var artifacts = root.GetProperty("artifacts");

        Assert.Equal(2, artifacts.GetArrayLength());

        var accounts = artifacts.EnumerateArray()
            .Single(artifact =>
                artifact.GetProperty("externalIdentifier").GetString()
                == "accounts.txt");

        Assert.Equal(
            "accounts.txt",
            accounts.GetProperty("name").GetString());

        Assert.Equal(
            "Account 1001 belongs to customer Alice.",
            accounts.GetProperty("content").GetString());

        var notes = artifacts.EnumerateArray()
            .Single(artifact =>
                artifact.GetProperty("externalIdentifier").GetString()
                == "notes.md");

        Assert.Equal(
            "notes.md",
            notes.GetProperty("name").GetString());

        Assert.Equal(
            "# Banking Notes\nCustomer account review.",
            notes.GetProperty("content").GetString());
    }
    finally
    {
        if (Directory.Exists(rootPath))
            Directory.Delete(rootPath, recursive: true);
    }
}

    [Fact]
public async Task Another_user_cannot_snapshot_knowledge_source()
{
    using var ownerClient = await CreateAuthenticatedClientAsync("user-a@test.local");
    using var otherClient = await CreateAuthenticatedClientAsync("user-b@test.local");

    var workspace = await CreateWorkspaceAsync(ownerClient);

    var rootPath = Path.Combine(
        Path.GetTempPath(),
        $"BankingBrainSnapshotIsolation_{Guid.NewGuid():N}");

    Directory.CreateDirectory(rootPath);

    try
    {
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "private.txt"),
            "Private banking information.");

        var createResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources",
            new
            {
                name = $"private-files-{Guid.NewGuid():N}",
                description = "Private filesystem source",
                sourceType = 1,
                connectorKey = "filesystem",
                externalIdentifier = rootPath
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var source = await createResponse.Content
            .ReadFromJsonAsync<KnowledgeSourceResponse>();

        Assert.NotNull(source);

        var snapshotResponse = await otherClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources/{source!.KnowledgeSourceId}/snapshot",
            new
            {
                revision = "unauthorized-attempt"
            });

        Assert.Equal(HttpStatusCode.NotFound, snapshotResponse.StatusCode);
    }
    finally
    {
        if (Directory.Exists(rootPath))
            Directory.Delete(rootPath, recursive: true);
    }
}

    [Fact]
    public async Task Owned_workspace_can_retrieve_a_knowledge_source_by_id()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);

        var response = await client.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources/{source.KnowledgeSourceId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Owned_workspace_can_create_and_list_documents()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents",
            new
            {
                knowledgeSourceId = source.KnowledgeSourceId,
                name = $"document-{Guid.NewGuid():N}",
                externalIdentifier = "core-banking-001",
                mimeType = "text/plain"
            });
        var document = await createResponse.Content
            .ReadFromJsonAsync<DocumentResponse>();
        var listResponse = await client.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents");
        var documents = await listResponse.Content
            .ReadFromJsonAsync<List<DocumentResponse>>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(documents!, item => item.DocumentId == document!.DocumentId);
    }

    [Fact]
    public async Task Owned_workspace_can_retrieve_a_document_by_id()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(
            client,
            workspace.WorkspaceId,
            source.KnowledgeSourceId);

        var response = await client.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_user_cannot_access_source_or_document()
    {
        using var ownerClient = await CreateAuthenticatedClientAsync("user-a@test.local");
        using var otherClient = await CreateAuthenticatedClientAsync("user-b@test.local");
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var source = await CreateKnowledgeSourceAsync(ownerClient, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(
            ownerClient,
            workspace.WorkspaceId,
            source.KnowledgeSourceId);

        var sourceResponse = await otherClient.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources/{source.KnowledgeSourceId}");
        var documentResponse = await otherClient.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}");
        var sourceListResponse = await otherClient.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/knowledge-sources");
        var documentListResponse = await otherClient.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents");

        Assert.Equal(HttpStatusCode.NotFound, sourceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, documentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sourceListResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, documentListResponse.StatusCode);
    }

    [Fact]
    public async Task Cross_workspace_document_source_relationship_is_rejected()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspaceA = await CreateWorkspaceAsync(client);
        var workspaceB = await CreateWorkspaceAsync(client);
        var sourceA = await CreateKnowledgeSourceAsync(client, workspaceA.WorkspaceId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceB.WorkspaceId}/documents",
            new
            {
                knowledgeSourceId = sourceA.KnowledgeSourceId,
                name = $"invalid-{Guid.NewGuid():N}",
                mimeType = "text/plain"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_knowledge_source_request_returns_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/v1/workspaces/{Guid.NewGuid()}/knowledge-sources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Document_versions_are_unique_and_support_multiple_sequential_versions()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(
            client,
            workspace.WorkspaceId,
            source.KnowledgeSourceId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.DocumentVersions.AddRange(
            DocumentVersion.Create(
                document.DocumentId,
                1,
                $"hash-{Guid.NewGuid():N}",
                "object://documents/v1",
                "version one"),
            DocumentVersion.Create(
                document.DocumentId,
                2,
                $"hash-{Guid.NewGuid():N}",
                "object://documents/v2",
                "version two"));
        await dbContext.SaveChangesAsync();

        var versions = await dbContext.DocumentVersions
            .Where(version => version.DocumentId == document.DocumentId)
            .OrderBy(version => version.VersionNumber)
            .ToListAsync();
        Assert.Equal(new[] { 1, 2 }, versions.Select(version => version.VersionNumber));

        dbContext.DocumentVersions.Add(
            DocumentVersion.Create(
                document.DocumentId,
                1,
                $"hash-{Guid.NewGuid():N}",
                null,
                null));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Ingestion_is_hash_idempotent_and_changed_content_creates_new_current_version()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(client, workspace.WorkspaceId, source.KnowledgeSourceId);
        var contentA = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("annual report one"));

        var first = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId, contentA);
        var duplicate = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId, contentA);
        var contentB = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("annual report two"));
        var changed = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId, contentB);
        var historicalDuplicate = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId, contentA);
        var versionsResponse = await client.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}/versions");
        var versions = await versionsResponse.Content
            .ReadFromJsonAsync<List<VersionResponse>>();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, historicalDuplicate.StatusCode);
        var firstVersion = await first.Content.ReadFromJsonAsync<IngestResponse>();
        var duplicateVersion = await duplicate.Content.ReadFromJsonAsync<IngestResponse>();
        var changedVersion = await changed.Content.ReadFromJsonAsync<IngestResponse>();
        var historicalVersion = await historicalDuplicate.Content.ReadFromJsonAsync<IngestResponse>();
        Assert.Equal(firstVersion!.DocumentVersionId, duplicateVersion!.DocumentVersionId);
        Assert.Equal(firstVersion.DocumentVersionId, historicalVersion!.DocumentVersionId);
        Assert.Equal(2, versions!.Count);
        Assert.Equal(new[] { 2, 1 }, versions.Select(version => version.VersionNumber));
        Assert.Equal(changedVersion!.DocumentVersionId, versions[0].DocumentVersionId);

        using (var jobScope = _factory.Services.CreateScope())
        {
            var jobDbContext = jobScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(await jobDbContext.ProcessingJobs.AnyAsync(job =>
                job.DocumentVersionId == changedVersion.DocumentVersionId));
        }

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedDocument = await dbContext.Documents
            .SingleAsync(item => item.Id == document.DocumentId);
        Assert.Equal(changedVersion.DocumentVersionId, persistedDocument.CurrentVersionId);
    }

    [Fact]
    public async Task Another_user_cannot_ingest_or_retrieve_versions()
    {
        using var ownerClient = await CreateAuthenticatedClientAsync("user-a@test.local");
        using var otherClient = await CreateAuthenticatedClientAsync("user-b@test.local");
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var source = await CreateKnowledgeSourceAsync(ownerClient, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(ownerClient, workspace.WorkspaceId, source.KnowledgeSourceId);
        var ingest = await IngestAsync(
            ownerClient,
            workspace.WorkspaceId,
            document.DocumentId,
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("private")));
        var version = await ingest.Content.ReadFromJsonAsync<IngestResponse>();

        var otherIngest = await IngestAsync(
            otherClient,
            workspace.WorkspaceId,
            document.DocumentId,
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("attack")));
        var otherVersions = await otherClient.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}/versions");
        var otherVersion = await otherClient.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}/versions/{version!.DocumentVersionId}");

        Assert.Equal(HttpStatusCode.NotFound, otherIngest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherVersions.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherVersion.StatusCode);
    }

    [Fact]
    public async Task Unsupported_document_type_is_rejected_before_storage()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(client, workspace.WorkspaceId, source.KnowledgeSourceId);

        var response = await IngestAsync(
            client,
            workspace.WorkspaceId,
            document.DocumentId,
            Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            "application/x-unknown",
            "file.unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Storage_is_workspace_scoped()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(client, workspace.WorkspaceId, source.KnowledgeSourceId);
        var content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("storage"));
        var ingest = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId, content);
        var version = await ingest.Content.ReadFromJsonAsync<IngestResponse>();

        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await dbContext.DocumentVersions.SingleAsync(item => item.Id == version!.DocumentVersionId);
        Assert.True(await storage.ExistsAsync(workspace.WorkspaceId.ToString(), persisted.StorageKey, default));
        Assert.False(await storage.ExistsAsync(Guid.NewGuid().ToString(), persisted.StorageKey, default));

        var contentResponse = await client.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}/versions/{version!.DocumentVersionId}/content");
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("storage", await contentResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Multipart_file_size_uses_actual_file_bytes_and_duplicate_pending_file_is_removed()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(client, workspace.WorkspaceId, source.KnowledgeSourceId);
        var first = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId,
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("exact-size")));
        var duplicate = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId,
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("exact-size")));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        var versions = await client.GetFromJsonAsync<List<VersionResponse>>(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}/versions");
        Assert.Equal(10, versions![0].FileSize);

        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var root = configuration["Storage:Local:RootPath"]!;
        var pending = Path.Combine(root, workspace.WorkspaceId.ToString(), ".pending");
        Assert.False(Directory.Exists(pending) && Directory.EnumerateFiles(pending).Any());
    }

    [Fact]
    public async Task Invalid_binary_content_is_rejected_for_text_upload()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(client, workspace.WorkspaceId, source.KnowledgeSourceId);
        var response = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId,
            Convert.ToBase64String(new byte[] { 0xFF, 0x00, 0x01 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_raw_storage_object_returns_not_found()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(client, workspace.WorkspaceId, source.KnowledgeSourceId);
        var ingest = await IngestAsync(client, workspace.WorkspaceId, document.DocumentId,
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("missing-object")));
        var version = await ingest.Content.ReadFromJsonAsync<IngestResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await db.DocumentVersions.SingleAsync(x => x.Id == version!.DocumentVersionId);
            await storage.DeleteAsync(workspace.WorkspaceId.ToString(), persisted.StorageKey, default);
        }

        var response = await client.GetAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}/versions/{version!.DocumentVersionId}/content");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reprocess_reuses_the_same_document_version()
    {
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateKnowledgeSourceAsync(client, workspace.WorkspaceId);
        var document = await CreateDocumentAsync(client, workspace.WorkspaceId, source.KnowledgeSourceId);
        var ingest = await IngestAsync(
            client,
            workspace.WorkspaceId,
            document.DocumentId,
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("reprocess")));
        var version = await ingest.Content.ReadFromJsonAsync<IngestResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tracked = await dbContext.DocumentVersions
                .SingleAsync(item => item.Id == version!.DocumentVersionId);
            tracked.MarkFailed("test failure", "test");
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            $"/api/v1/workspaces/{workspace.WorkspaceId}/documents/{document.DocumentId}/versions/{version!.DocumentVersionId}/reprocess",
            content: null);
        var body = await response.Content.ReadFromJsonAsync<ReprocessResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(version.DocumentVersionId, body!.DocumentVersionId);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = new PasswordHasher();

        if (!await dbContext.Users.AnyAsync(user => user.Email == email))
        {
            dbContext.Users.Add(User.Create(
                email,
                hasher.Hash("Password@123"),
                email[..email.IndexOf('@')]));
            await dbContext.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = "Password@123" });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static async Task<WorkspaceResponse> CreateWorkspaceAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/workspaces",
            new { name = $"workspace-{Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceResponse>())!;
    }

    private static async Task<KnowledgeSourceResponse> CreateKnowledgeSourceAsync(
        HttpClient client,
        Guid workspaceId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/knowledge-sources",
            new
            {
                name = $"source-{Guid.NewGuid():N}",
                description = "Test source",
                sourceType = 6
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content
            .ReadFromJsonAsync<KnowledgeSourceResponse>())!;
    }

    private static async Task<DocumentResponse> CreateDocumentAsync(
        HttpClient client,
        Guid workspaceId,
        Guid knowledgeSourceId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/documents",
            new
            {
                knowledgeSourceId,
                name = $"document-{Guid.NewGuid():N}",
                mimeType = "text/plain"
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentResponse>())!;
    }

    private static Task<HttpResponseMessage> IngestAsync(
        HttpClient client,
        Guid workspaceId,
        Guid documentId,
        string contentBase64,
        string mediaType = "text/plain",
        string fileName = "document.txt")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Convert.FromBase64String(contentBase64));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        content.Add(file, "file", fileName);
        return client.PostAsync(
            $"/api/v1/workspaces/{workspaceId}/documents/{documentId}/ingest",
            content);
    }

    private sealed record LoginResponse(
        Guid UserId,
        string FullName,
        string Email,
        string AccessToken,
        string RefreshToken);

    private sealed record WorkspaceResponse(Guid WorkspaceId);

    private sealed record KnowledgeSourceResponse(
        Guid KnowledgeSourceId,
        Guid WorkspaceId);

    private sealed record DocumentResponse(
        Guid DocumentId,
        Guid WorkspaceId,
        Guid KnowledgeSourceId);

    private sealed record IngestResponse(
        Guid DocumentVersionId,
        Guid DocumentId,
        int VersionNumber,
        string ContentHash,
        DocumentProcessingState ProcessingState,
        bool AlreadyCurrent);

    private sealed record VersionResponse(
        Guid DocumentVersionId,
        Guid DocumentId,
        int VersionNumber,
        string ContentHash,
        string MediaType,
        string FileName,
        long FileSize,
        DocumentProcessingState ProcessingState,
        DateTime CreatedOnUtc,
        DateTime? ProcessingStartedOnUtc,
        DateTime? ProcessingCompletedOnUtc,
        string? ProcessingError,
        string? ProcessorVersion);

    private sealed record ReprocessResponse(
        Guid DocumentVersionId,
        DocumentProcessingState ProcessingState);
}
