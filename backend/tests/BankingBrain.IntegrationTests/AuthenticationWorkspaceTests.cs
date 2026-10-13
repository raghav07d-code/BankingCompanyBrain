using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BankingBrain.Domain.Entities;
using BankingBrain.Infrastructure.Identity.Passwords;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class AuthenticationWorkspaceTests
    : IClassFixture<BankingBrainApiFactory>
{
    private const string JwtSecret =
        "THIS_IS_A_DEVELOPMENT_SECRET_KEY_CHANGE_IN_PRODUCTION_32CHARS_MIN";

    private readonly BankingBrainApiFactory _factory;

    public AuthenticationWorkspaceTests(BankingBrainApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_request_to_protected_endpoint_returns_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_can_create_and_list_own_workspace()
    {
        await EnsureUsersAsync();
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var name = $"workspace-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/workspaces",
            new { name, description = "Test workspace" });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var workspace = await createResponse.Content.ReadFromJsonAsync<WorkspaceResponse>();
        Assert.NotNull(workspace);

        var listResponse = await client.GetAsync("/api/v1/workspaces");
        var workspaces = await listResponse.Content
            .ReadFromJsonAsync<List<WorkspaceResponse>>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(workspaces!, item => item.WorkspaceId == workspace!.WorkspaceId);
    }

    [Fact]
    public async Task Different_user_cannot_see_or_retrieve_another_users_workspace()
    {
        await EnsureUsersAsync();
        using var ownerClient = await CreateAuthenticatedClientAsync("user-a@test.local");
        using var otherClient = await CreateAuthenticatedClientAsync("user-b@test.local");
        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/workspaces",
            new { name = $"private-{Guid.NewGuid():N}" });
        var workspace = await createResponse.Content.ReadFromJsonAsync<WorkspaceResponse>();

        var listResponse = await otherClient.GetAsync("/api/v1/workspaces");
        var workspaces = await listResponse.Content
            .ReadFromJsonAsync<List<WorkspaceResponse>>();
        var retrieveResponse = await otherClient.GetAsync(
            $"/api/v1/workspaces/{workspace!.WorkspaceId}");

        Assert.DoesNotContain(workspaces!, item => item.WorkspaceId == workspace.WorkspaceId);
        Assert.Equal(HttpStatusCode.NotFound, retrieveResponse.StatusCode);
    }

    [Fact]
    public async Task Duplicate_workspace_name_is_allowed_for_different_owners()
    {
        await EnsureUsersAsync();
        var name = $"shared-name-{Guid.NewGuid():N}";
        using var ownerClient = await CreateAuthenticatedClientAsync("user-a@test.local");
        using var otherClient = await CreateAuthenticatedClientAsync("user-b@test.local");

        var first = await ownerClient.PostAsJsonAsync(
            "/api/v1/workspaces", new { name });
        var second = await otherClient.PostAsJsonAsync(
            "/api/v1/workspaces", new { name });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Duplicate_workspace_name_is_rejected_for_same_owner()
    {
        await EnsureUsersAsync();
        var name = $"duplicate-{Guid.NewGuid():N}";
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");

        var first = await client.PostAsJsonAsync(
            "/api/v1/workspaces", new { name });
        var second = await client.PostAsJsonAsync(
            "/api/v1/workspaces", new { name });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Concurrent_duplicate_workspace_creates_return_one_success_and_one_conflict()
    {
        await EnsureUsersAsync();
        using var client = await CreateAuthenticatedClientAsync("user-a@test.local");
        var name = $"concurrent-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(
            CreateWorkspaceAsync(client, name),
            CreateWorkspaceAsync(client, name));

        Assert.Equal(
            1,
            responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(
            1,
            responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

        var conflictResponse = responses.Single(
            response => response.StatusCode == HttpStatusCode.Conflict);
        var conflictBody = await conflictResponse.Content.ReadAsStringAsync();
        Assert.Contains("WORKSPACE.DUPLICATE", conflictBody);

        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task Login_is_case_insensitive_for_email()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "AdMiN@BrAiN.Ai", password = "Password@123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_jwt_subject_is_rejected_without_an_unhandled_exception()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken("not-a-guid"));

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
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

    private static Task<HttpResponseMessage> CreateWorkspaceAsync(
        HttpClient client,
        string name)
    {
        return client.PostAsJsonAsync(
            "/api/v1/workspaces",
            new { name });
    }

    private async Task EnsureUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = new PasswordHasher();

        foreach (var email in new[] { "user-a@test.local", "user-b@test.local" })
        {
            if (await dbContext.Users.AnyAsync(user => user.Email == email))
                continue;

            dbContext.Users.Add(User.Create(
                email,
                hasher.Hash("Password@123"),
                email[..email.IndexOf('@')]));
        }

        await dbContext.SaveChangesAsync();
    }

    private static string CreateToken(string subject)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "BankingCompanyBrain",
            audience: "BankingCompanyBrain",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject)
            },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record LoginResponse(
        Guid UserId,
        string FullName,
        string Email,
        string AccessToken,
        string RefreshToken);

    private sealed record WorkspaceResponse(
        Guid WorkspaceId,
        string Name,
        string? Description,
        bool IsActive);
}
