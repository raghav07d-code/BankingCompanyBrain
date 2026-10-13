using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BankingBrain.Domain.Entities;
using BankingBrain.Domain.Entities.DocumentSection;
using BankingBrain.Domain.Entities.DocumentVersion;
using BankingBrain.Domain.Enums;
using BankingBrain.Application.Common.Models;
using BankingBrain.Application.Common.Embeddings;
using BankingBrain.Application.Common.Graph;
using BankingBrain.Application.Common.GroundedIntelligence;
using BankingBrain.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Application.Features.Semantics.Queries;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Infrastructure.Identity.Passwords;
using BankingBrain.Infrastructure.Persistence.Context;
using BankingBrain.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BankingBrain.IntegrationTests;

public sealed class SemanticExtractionTests : IClassFixture<BankingBrainApiFactory>
{
    private readonly BankingBrainApiFactory _factory;

    public SemanticExtractionTests(BankingBrainApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Extraction_creates_claim_relationship_and_exact_evidence()
    {
        using var client = await CreateAuthenticatedClientAsync("semantic-a@test.local");
        var fixture = await CreateProcessedVersionAsync(client, "Acme owns Loan");

        var trigger = await client.PostAsync(fixture.ExtractionUrl, null);
        var queued = await trigger.Content.ReadFromJsonAsync<RunResponse>();
        var run = await WaitForSucceededAsync(client, fixture.RunsUrl);
        var claims = await client.GetFromJsonAsync<List<ClaimResponse>>(fixture.ClaimsUrl);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(HttpStatusCode.Accepted, trigger.StatusCode);
        Assert.NotNull(queued);
        Assert.Contains(queued!.Status, new[] { SemanticExtractionRunStatus.Pending, SemanticExtractionRunStatus.Running, SemanticExtractionRunStatus.Succeeded });
        Assert.Equal(SemanticExtractionRunStatus.Succeeded, run.Status);
        Assert.NotEmpty(claims!);
        Assert.Contains(claims!, claim => claim.Subject == "Acme" && claim.Predicate == "owns" && claim.ObjectValue == "Loan");
        Assert.True(await db.SemanticRelationships.AnyAsync(relation => relation.DocumentVersionId == fixture.VersionId && relation.RelationType == "owns"));

        var claim = claims!.Single(item => item.Subject == "Acme");
        var details = await client.GetFromJsonAsync<ClaimDetailsResponse>(
            $"/api/v1/workspaces/{fixture.WorkspaceId}/semantic/claims/{claim.ClaimId}");
        Assert.Equal("document", details!.Evidence.SourceLocation);
        Assert.Equal(fixture.VersionId, details.Evidence.DocumentVersionId);
        Assert.True(await db.SemanticProcessingJobs.AnyAsync(job =>
            job.DocumentVersionId == fixture.VersionId && job.Status == ProcessingJobStatus.Completed));
    }

    [Fact]
    public async Task Repeated_extraction_is_idempotent_and_semantic_data_is_isolated()
    {
        using var owner = await CreateAuthenticatedClientAsync("semantic-b@test.local");
        using var other = await CreateAuthenticatedClientAsync("semantic-c@test.local");
        var fixture = await CreateProcessedVersionAsync(owner, "Acme owns Loan");

        var first = await owner.PostAsync(fixture.ExtractionUrl, null);
        var firstRun = await WaitForSucceededAsync(owner, fixture.RunsUrl);
        var second = await owner.PostAsync(fixture.ExtractionUrl, null);
        var claims = await owner.GetFromJsonAsync<List<ClaimResponse>>(fixture.ClaimsUrl);
        var otherClaims = await other.GetAsync(fixture.ClaimsUrl);

        var secondRun = await second.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(firstRun.RunId, secondRun!.ExtractionRunId);
        Assert.Equal(1, claims!.Count(claim => claim.Subject == "Acme" && claim.Predicate == "owns"));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.SemanticProcessingJobs.CountAsync(job => job.DocumentVersionId == fixture.VersionId));
        }
        Assert.Equal(HttpStatusCode.NotFound, otherClaims.StatusCode);
    }

    [Fact]
    public async Task External_identifiers_resolve_same_entities_without_merging_distinct_entities()
    {
        using var client = await CreateAuthenticatedClientAsync("semantic-entities@test.local");
        var fixture = await CreateProcessedVersionAsync(client, "entity evidence");

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISemanticExtractionRepository>();
        var source = await CreateSemanticSourceAsync(scope.ServiceProvider, fixture);

        await PersistResultAsync(repository, source, new SemanticExtractionResult(
            new[]
            {
                new SemanticEntityCandidate("INSTITUTION", "First Bank", Array.Empty<string>(), .9m, 0, "bank-001"),
                new SemanticEntityCandidate("INSTITUTION", "Second Bank", Array.Empty<string>(), .9m, 0, "bank-002")
            }, Array.Empty<SemanticClaimCandidate>(), Array.Empty<SemanticRelationshipCandidate>()));
        await PersistResultAsync(repository, source, new SemanticExtractionResult(
            new[]
            {
                new SemanticEntityCandidate("INSTITUTION", "First Bank Renamed", Array.Empty<string>(), .95m, 0, "bank-001"),
                new SemanticEntityCandidate("INSTITUTION", "Second Bank", Array.Empty<string>(), .95m, 0, "bank-002")
            }, Array.Empty<SemanticClaimCandidate>(), Array.Empty<SemanticRelationshipCandidate>()));

        var entities = await repository.GetEntitiesAsync(fixture.WorkspaceId, CancellationToken.None);
        Assert.Equal(2, entities.Count(entity => entity.EntityType == "INSTITUTION"));
        Assert.Contains(entities, entity => entity.ExternalIdentifier == "bank-001" && entity.CanonicalName == "First Bank");
        Assert.Contains(entities, entity => entity.ExternalIdentifier == "bank-002" && entity.CanonicalName == "Second Bank");
    }

    [Fact]
    public async Task Temporal_claim_queries_filter_as_of_and_validity_periods()
    {
        using var client = await CreateAuthenticatedClientAsync("semantic-temporal@test.local");
        var fixture = await CreateProcessedVersionAsync(client, "temporal evidence");

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISemanticExtractionRepository>();
        var source = await CreateSemanticSourceAsync(scope.ServiceProvider, fixture);
        var first = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await PersistResultAsync(repository, source, new SemanticExtractionResult(
            Array.Empty<SemanticEntityCandidate>(),
            new[]
            {
                new SemanticClaimCandidate("Acme", "HAS_RATING", "BBB", .9m, 0, "INSTITUTION", AsOfUtc: first, ValidFromUtc: first, ValidToUtc: second),
                new SemanticClaimCandidate("Acme", "HAS_RATING", "BBB+", .9m, 0, "INSTITUTION", AsOfUtc: second, ValidFromUtc: second)
            }, Array.Empty<SemanticRelationshipCandidate>()));

        var asOfClaims = await repository.GetClaimsAsync(fixture.WorkspaceId, fixture.VersionId, null, "HAS_RATING", first, null, CancellationToken.None);
        var validClaims = await repository.GetClaimsAsync(fixture.WorkspaceId, fixture.VersionId, null, "HAS_RATING", null, second, CancellationToken.None);

        Assert.Single(asOfClaims);
        Assert.Equal("BBB", asOfClaims[0].ObjectValue);
        Assert.Equal(2, validClaims.Count);
    }

    [Fact]
    public async Task Newer_extractor_version_reprocessing_preserves_prior_successful_output()
    {
        using var client = await CreateAuthenticatedClientAsync("semantic-reprocess@test.local");
        var fixture = await CreateProcessedVersionAsync(client, "Acme owns Loan");

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISemanticExtractionRepository>();
        var source = await CreateSemanticSourceAsync(scope.ServiceProvider, fixture);
        var result = new SemanticExtractionResult(
            new[] { new SemanticEntityCandidate("ORGANIZATION", "Acme", Array.Empty<string>(), .9m, 0), new SemanticEntityCandidate("LOAN_FACILITY", "Loan", Array.Empty<string>(), .9m, 0) },
            new[] { new SemanticClaimCandidate("Acme", "OWNS", "Loan", .9m, 0, "ORGANIZATION", "LOAN_FACILITY") },
            new[] { new SemanticRelationshipCandidate("Acme", "OWNS", "Loan", .9m, 0, "ORGANIZATION", "LOAN_FACILITY") });

        await PersistResultAsync(repository, source, result, "deterministic-rules", "1.0");
        await PersistResultAsync(repository, source, result, "deterministic-rules", "2.0");

        var runs = await repository.GetRunsAsync(fixture.WorkspaceId, fixture.VersionId, CancellationToken.None);
        var claims = await repository.GetClaimsAsync(fixture.WorkspaceId, fixture.VersionId, null, null, null, null, CancellationToken.None);
        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(SemanticExtractionRunStatus.Succeeded, run.Status));
        Assert.Single(claims);
        Assert.Contains(claims[0].ExtractionRunId, runs.Select(run => run.Id));
    }

    [Fact]
    public async Task Vocabulary_is_workspace_isolated_and_enforces_object_types()
    {
        using var firstClient = await CreateAuthenticatedClientAsync("semantic-vocabulary-a@test.local");
        using var secondClient = await CreateAuthenticatedClientAsync("semantic-vocabulary-b@test.local");
        var firstWorkspace = await CreateWorkspaceAsync(firstClient);
        var secondWorkspace = await CreateWorkspaceAsync(secondClient);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SemanticEntityTypes.Add(SemanticEntityType.Create(firstWorkspace, "CUSTOM_BANK", "Custom Bank"));
            db.SemanticPredicates.Add(SemanticPredicate.Create(firstWorkspace, "FINANCES", "Finances", true, new[] { "INSTITUTION" }, new[] { "ACCOUNT" }));
            await db.SaveChangesAsync();
        }

        var firstTypes = await firstClient.GetFromJsonAsync<List<SemanticVocabularyResponse>>($"/api/v1/workspaces/{firstWorkspace}/semantic/entity-types");
        var secondTypes = await secondClient.GetFromJsonAsync<List<SemanticVocabularyResponse>>($"/api/v1/workspaces/{secondWorkspace}/semantic/entity-types");
        var firstPredicates = await firstClient.GetFromJsonAsync<List<SemanticPredicateResponse>>($"/api/v1/workspaces/{firstWorkspace}/semantic/predicates");
        var secondPredicates = await secondClient.GetFromJsonAsync<List<SemanticPredicateResponse>>($"/api/v1/workspaces/{secondWorkspace}/semantic/predicates");

        Assert.Contains(firstTypes!, item => item.Code == "CUSTOM_BANK");
        Assert.DoesNotContain(secondTypes!, item => item.Code == "CUSTOM_BANK");
        Assert.Contains(firstPredicates!, item => item.Code == "FINANCES");
        Assert.DoesNotContain(secondPredicates!, item => item.Code == "FINANCES");
        using var validationScope = _factory.Services.CreateScope();
        var vocabulary = validationScope.ServiceProvider.GetRequiredService<IBankingSemanticVocabulary>();
        Assert.True(await vocabulary.IsValidAsync(firstWorkspace, "INSTITUTION", "FINANCES", "ACCOUNT", CancellationToken.None));
        Assert.False(await vocabulary.IsValidAsync(firstWorkspace, "INSTITUTION", "FINANCES", "PERSON", CancellationToken.None));
        Assert.False(await vocabulary.IsValidAsync(secondWorkspace, "INSTITUTION", "FINANCES", "ACCOUNT", CancellationToken.None));
    }

    [Fact]
    public async Task Deterministic_queries_return_latest_temporal_conflicts_provenance_and_derived_relationships()
    {
        using var owner = await CreateAuthenticatedClientAsync("semantic-query-owner@test.local");
        using var other = await CreateAuthenticatedClientAsync("semantic-query-other@test.local");
        var fixture = await CreateProcessedVersionAsync(owner, "query evidence");

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISemanticExtractionRepository>();
        var queries = scope.ServiceProvider.GetRequiredService<ISemanticQueryService>();
        var source = await CreateSemanticSourceAsync(scope.ServiceProvider, fixture);
        var firstDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = new SemanticExtractionResult(
            new[]
            {
                new SemanticEntityCandidate("INSTITUTION", "Acme Bank", new[] { "Acme" }, .95m, 0, "bank-query-1"),
                new SemanticEntityCandidate("LOAN_FACILITY", "Loan 7", Array.Empty<string>(), .95m, 0, "loan-query-7")
            },
            new[]
            {
                new SemanticClaimCandidate("Acme Bank", "HAS_RATING", "BBB", .8m, 0, "INSTITUTION", AsOfUtc: firstDate, ValidFromUtc: firstDate, ValidToUtc: secondDate),
                new SemanticClaimCandidate("Acme Bank", "HAS_RATING", "BBB+", .9m, 0, "INSTITUTION", AsOfUtc: secondDate, ValidFromUtc: secondDate),
                new SemanticClaimCandidate("Acme Bank", "OWNS", "Loan 7", .9m, 0, "INSTITUTION", "LOAN_FACILITY")
            },
            new[] { new SemanticRelationshipCandidate("Acme Bank", "OWNS", "Loan 7", .9m, 0, "INSTITUTION", "LOAN_FACILITY") });
        await PersistResultAsync(repository, source, result, "query-test", "1.0");

        var entity = await queries.FindEntityAsync(fixture.WorkspaceId, "INSTITUTION", "Acme", "bank-query-1", CancellationToken.None);
        Assert.NotNull(entity);
        var claims = await queries.QueryClaimsAsync(fixture.WorkspaceId, new SemanticClaimQuery("Acme Bank", "HAS_RATING"), CancellationToken.None);
        var asOf = await queries.QueryClaimsAsync(fixture.WorkspaceId, new SemanticClaimQuery("Acme Bank", "HAS_RATING", AsOfUtc: firstDate), CancellationToken.None);
        var period = await queries.QueryClaimsAsync(fixture.WorkspaceId, new SemanticClaimQuery("Acme Bank", "HAS_RATING", ValidFromUtc: secondDate.AddMonths(-1), ValidToUtc: secondDate.AddMonths(1)), CancellationToken.None);
        var latest = await queries.GetLatestClaimAsync(fixture.WorkspaceId, new SemanticClaimQuery("Acme Bank", "HAS_RATING"), CancellationToken.None);

        Assert.Equal(2, claims.Claims.Count);
        Assert.Single(asOf.Claims);
        Assert.Equal("BBB", asOf.Claims[0].ObjectValue);
        Assert.Equal(2, period.Claims.Count);
        Assert.Equal("BBB+", latest!.ObjectValue);
        Assert.NotEmpty(claims.Evidence);

        var loan = await queries.FindEntityAsync(fixture.WorkspaceId, "LOAN_FACILITY", "Loan 7", "loan-query-7", CancellationToken.None);
        var relationships = await queries.QueryRelationshipsAsync(fixture.WorkspaceId, new SemanticRelationshipQuery(entity!.EntityId, loan!.EntityId, "OWNS"), CancellationToken.None);
        Assert.Contains(relationships.Relationships, relationship => !relationship.IsDerived);
        Assert.Contains(relationships.Relationships, relationship => relationship.IsDerived && relationship.SupportingClaimIds.Count == 1);

        var foreignWorkspaceResponse = await other.GetAsync($"/api/v1/workspaces/{fixture.WorkspaceId}/semantic/query/claims?predicate=HAS_RATING");
        Assert.Equal(HttpStatusCode.NotFound, foreignWorkspaceResponse.StatusCode);
    }

    [Fact]
    public async Task Embedding_search_is_workspace_scoped_filtered_provenance_rich_and_idempotent()
    {
        using var firstClient = await CreateAuthenticatedClientAsync("semantic-embedding-a@test.local");
        using var secondClient = await CreateAuthenticatedClientAsync("semantic-embedding-b@test.local");
        var firstWorkspace = await CreateWorkspaceAsync(firstClient);
        var secondWorkspace = await CreateWorkspaceAsync(secondClient);
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISemanticEmbeddingService>();
        var sourceId = Guid.NewGuid();
        var firstDocument = EmbeddingDocument("bank balance", firstWorkspace, sourceId, Guid.NewGuid(), "alpha");
        var secondDocument = EmbeddingDocument("bank balance", secondWorkspace, sourceId, Guid.NewGuid(), "beta");

        await service.IndexAsync(firstWorkspace, new[] { firstDocument }, CancellationToken.None);
        await service.IndexAsync(firstWorkspace, new[] { firstDocument }, CancellationToken.None);
        await service.IndexAsync(secondWorkspace, new[] { secondDocument }, CancellationToken.None);

        var firstResults = await service.SearchAsync(firstWorkspace, new SemanticEmbeddingSearchRequest("bank balance", 10, new SemanticEmbeddingFilter(SourceType: "DOCUMENT_SECTION")), CancellationToken.None);
        var secondResults = await service.SearchAsync(secondWorkspace, new SemanticEmbeddingSearchRequest("bank balance"), CancellationToken.None);
        Assert.Single(firstResults);
        Assert.Single(secondResults);
        Assert.Equal(firstWorkspace.ToString(), firstResults[0].Metadata["workspace"]);
        Assert.Equal("DOCUMENT_SECTION", firstResults[0].SourceType);
        Assert.Equal(firstDocument.DocumentSectionId, firstResults[0].DocumentSectionId);
        Assert.Equal(firstDocument.DocumentVersionId, firstResults[0].DocumentVersionId);
        Assert.NotEqual(firstResults[0].EmbeddingId, secondResults[0].EmbeddingId);
    }

    [Fact]
    public async Task Embedding_reindex_replaces_stale_vector_and_provider_failure_preserves_previous_index()
    {
        using var client = await CreateAuthenticatedClientAsync("semantic-embedding-reindex@test.local");
        var workspace = await CreateWorkspaceAsync(client);
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISemanticEmbeddingStore>();
        var stableProvider = new TestEmbeddingProvider("test", "1", "fake");
        var service = new BankingBrain.Infrastructure.Processing.SemanticEmbeddingService(stableProvider, store);
        var sourceId = Guid.NewGuid();
        var original = EmbeddingDocument("original banking fact", workspace, sourceId, Guid.NewGuid(), "reindex");
        var replacement = original with { Content = "replacement banking fact", ContentHash = BankingBrain.Infrastructure.Processing.SemanticEmbeddingService.HashContent("replacement banking fact") };

        await service.IndexAsync(workspace, new[] { original }, CancellationToken.None);
        await service.IndexAsync(workspace, new[] { replacement }, CancellationToken.None);
        var results = await service.SearchAsync(workspace, new SemanticEmbeddingSearchRequest("replacement banking fact"), CancellationToken.None);
        Assert.Single(results);
        Assert.Equal(replacement.ContentHash, await GetEmbeddingContentHashAsync(scope.ServiceProvider, results[0].EmbeddingId));

        var failing = new BankingBrain.Infrastructure.Processing.SemanticEmbeddingService(new TestEmbeddingProvider("failing", "1", "fake", true), store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.IndexAsync(workspace, new[] { replacement with { Content = "failed update" } }, CancellationToken.None));
        Assert.Single(await service.SearchAsync(workspace, new SemanticEmbeddingSearchRequest("replacement banking fact"), CancellationToken.None));
    }

    [Fact]
    public async Task Graph_projection_is_idempotent_workspace_scoped_temporal_and_bounded_with_provenance()
    {
        using var firstClient = await CreateAuthenticatedClientAsync("semantic-graph-a@test.local");
        using var secondClient = await CreateAuthenticatedClientAsync("semantic-graph-b@test.local");
        var firstFixture = await CreateProcessedVersionAsync(firstClient, "graph evidence");
        var secondFixture = await CreateProcessedVersionAsync(secondClient, "graph evidence");
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISemanticExtractionRepository>();
        var graph = scope.ServiceProvider.GetRequiredService<ISemanticGraphService>();
        var firstSource = await CreateSemanticSourceAsync(scope.ServiceProvider, firstFixture);
        var secondSource = await CreateSemanticSourceAsync(scope.ServiceProvider, secondFixture);
        var firstDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstResult = GraphResult("Bank A", "Bank B", "Fund C", firstDate, secondDate);
        await PersistResultAsync(repository, firstSource, firstResult);
        await PersistResultAsync(repository, secondSource, GraphResult("Bank A", "Bank B", "Fund C", firstDate, secondDate));
        await graph.RebuildAsync(firstFixture.WorkspaceId, CancellationToken.None);
        await graph.RebuildAsync(firstFixture.WorkspaceId, CancellationToken.None);
        await graph.RebuildAsync(secondFixture.WorkspaceId, CancellationToken.None);

        var firstEntities = await repository.GetEntitiesAsync(firstFixture.WorkspaceId, CancellationToken.None);
        var bankA = firstEntities.Single(entity => entity.CanonicalName == "Bank A");
        var bankB = firstEntities.Single(entity => entity.CanonicalName == "Bank B");
        var fundC = firstEntities.Single(entity => entity.CanonicalName == "Fund C");
        var direct = await graph.QueryAsync(firstFixture.WorkspaceId, new SemanticGraphQuery(bankA.Id, bankB.Id, "OWNS", ValidAtUtc: secondDate, MaxDepth: 1), CancellationToken.None);
        var bounded = await graph.QueryAsync(firstFixture.WorkspaceId, new SemanticGraphQuery(bankA.Id, null, null, MaxDepth: 1), CancellationToken.None);
        var twoHop = await graph.QueryAsync(firstFixture.WorkspaceId, new SemanticGraphQuery(bankA.Id, fundC.Id, null, MaxDepth: 2), CancellationToken.None);
        var old = await graph.QueryAsync(firstFixture.WorkspaceId, new SemanticGraphQuery(bankA.Id, null, "OWNS", ValidAtUtc: firstDate), CancellationToken.None);
        var foreign = await graph.QueryAsync(secondFixture.WorkspaceId, new SemanticGraphQuery(bankA.Id, null, null, MaxDepth: 2), CancellationToken.None);
        using var dbScope = _factory.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Contains(direct.Edges, edge => edge.SubjectEntityId == bankA.Id && edge.ObjectEntityId == bankB.Id);
        Assert.All(bounded.Edges, edge => Assert.Equal(bankA.Id, edge.SubjectEntityId));
        Assert.Contains(twoHop.Edges, edge => edge.ObjectEntityId == fundC.Id);
        Assert.NotEmpty(old.Edges);
        Assert.Empty(foreign.Edges);
        Assert.Equal(3, await db.SemanticGraphNodes.CountAsync(node => node.WorkspaceId == firstFixture.WorkspaceId));
        Assert.Equal(5, await db.SemanticGraphEdges.CountAsync(edge => edge.WorkspaceId == firstFixture.WorkspaceId));
        Assert.Contains(direct.Edges, edge => edge.EvidenceIds.Count > 0 && edge.SupportingClaimIds.Count > 0);
    }

    [Fact]
    public async Task Grounded_intelligence_composes_exact_vector_and_graph_context_and_surfaces_conflicts()
    {
        using var client = await CreateAuthenticatedClientAsync("semantic-grounded@test.local");
        var fixture = await CreateProcessedVersionAsync(client, "Acme owns Loan");
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISemanticExtractionRepository>();
        var source = await CreateSemanticSourceAsync(scope.ServiceProvider, fixture);
        await PersistResultAsync(repository, source, new SemanticExtractionResult(
            new[] { new SemanticEntityCandidate("INSTITUTION", "Acme", Array.Empty<string>(), .9m, 0), new SemanticEntityCandidate("LOAN_FACILITY", "Loan", Array.Empty<string>(), .9m, 0) },
            new[]
            {
                new SemanticClaimCandidate("Acme", "OWNS", "Loan", .9m, 0, "INSTITUTION", "LOAN_FACILITY", AsOfUtc: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new SemanticClaimCandidate("Acme", "OWNS", "Different Loan", .8m, 0, "INSTITUTION", "LOAN_FACILITY", AsOfUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            },
            new[] { new SemanticRelationshipCandidate("Acme", "OWNS", "Loan", .9m, 0, "INSTITUTION", "LOAN_FACILITY") }));
        var graph = scope.ServiceProvider.GetRequiredService<ISemanticGraphService>();
        await graph.RebuildAsync(fixture.WorkspaceId, CancellationToken.None);
        var embeddings = scope.ServiceProvider.GetRequiredService<ISemanticEmbeddingService>();
        var text = "Acme OWNS Loan";
        await embeddings.IndexAsync(fixture.WorkspaceId, new[] { new SemanticEmbeddingDocument("DOCUMENT_SECTION", Guid.NewGuid(), fixture.VersionId, Guid.NewGuid(), null, null, Guid.NewGuid(), text, BankingBrain.Infrastructure.Processing.SemanticEmbeddingService.HashContent(text), new Dictionary<string, string> { ["source.location"] = "document" }) }, CancellationToken.None);

        var intelligence = scope.ServiceProvider.GetRequiredService<IGroundedIntelligenceService>();
        var answer = await intelligence.AnswerAsync(fixture.WorkspaceId, new GroundedQuestion("What does Acme OWNS?", AsOfUtc: new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc), MaxGraphDepth: 1), CancellationToken.None);

        Assert.Equal("conflicting-evidence", answer.EvidenceStatus);
        Assert.NotEmpty(answer.Citations);
        Assert.Contains(answer.Context, item => item.SemanticClaimId is not null);
        Assert.Contains(answer.Context, item => item.Text.Contains("Loan", StringComparison.Ordinal));
        Assert.Contains(answer.Context, item => item.Text.Contains("Different Loan", StringComparison.Ordinal));
        Assert.Contains(answer.Context, item => item.RelationshipId is not null);
        Assert.Contains("conflicting observations", answer.Answer);
    }

    [Fact]
    public async Task Grounded_intelligence_returns_controlled_insufficient_evidence_and_is_workspace_scoped()
    {
        using var owner = await CreateAuthenticatedClientAsync("semantic-grounded-empty@test.local");
        using var other = await CreateAuthenticatedClientAsync("semantic-grounded-foreign@test.local");
        var ownerWorkspace = await CreateWorkspaceAsync(owner);
        var otherWorkspace = await CreateWorkspaceAsync(other);
        using var scope = _factory.Services.CreateScope();
        var intelligence = scope.ServiceProvider.GetRequiredService<IGroundedIntelligenceService>();

        var answer = await intelligence.AnswerAsync(ownerWorkspace, new GroundedQuestion("What does OWNS say?"), CancellationToken.None);
        var foreignAnswer = await intelligence.AnswerAsync(otherWorkspace, new GroundedQuestion("What does OWNS say?"), CancellationToken.None);

        Assert.Equal("insufficient-evidence", answer.EvidenceStatus);
        Assert.Empty(answer.Citations);
        Assert.Equal("insufficient-evidence", foreignAnswer.EvidenceStatus);
        Assert.Empty(foreignAnswer.Context);
    }

    [Fact]
    public async Task Configured_generator_rejects_unknown_citations_and_reports_provider_failure()
    {
        var evidenceId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var context = new GroundedContext(new[] { new GroundedContextItem("claim", "Acme OWNS Loan", Guid.NewGuid(), null, null, claimId, null, evidenceId, "page:1", null, null, null, .9m, null) }, new QuestionIntent("OWNS", "Acme", true, true, false));
        var options = Options.Create(new LlmOptions { Provider = "http", Model = "fake", MaxContextCharacters = 12000 });
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BankingBrain.Infrastructure.Processing.ConfiguredGroundedAnswerGenerator>.Instance;
        var success = new BankingBrain.Infrastructure.Processing.ConfiguredGroundedAnswerGenerator(new FakeLlmProvider(new LlmGenerationResult("grounded answer", "grounded", new[] { $"claim:{claimId}", $"claim:{Guid.NewGuid()}" }, "fake", "1", "fake")), options, logger);
        var answer = await success.GenerateAsync(context, Guid.NewGuid(), CancellationToken.None);
        Assert.Single(answer.Citations);
        Assert.Equal(claimId, answer.Citations[0].SemanticClaimId);
        var failure = new BankingBrain.Infrastructure.Processing.ConfiguredGroundedAnswerGenerator(new FakeLlmProvider(null, true), options, logger);
        var failed = await failure.GenerateAsync(context, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("provider-failure", failed.EvidenceStatus);
        Assert.Empty(failed.Citations);
    }

    [Fact]
    public async Task Grounded_context_budget_keeps_whole_provenance_items()
    {
        var first = new GroundedContextItem("claim", new string('a', 700), Guid.NewGuid(), null, null, Guid.NewGuid(), null, Guid.NewGuid(), "page:1", null, null, null, .9m, null);
        var second = first with { Text = new string('b', 700), SemanticClaimId = Guid.NewGuid(), EvidenceId = Guid.NewGuid(), SourceLocation = "page:2" };
        var retriever = new FakeContextRetriever(new GroundedContext(new[] { first, second }, new QuestionIntent(null, null, false, false, true)));
        var generator = new CapturingGenerator();
        var service = new BankingBrain.Infrastructure.Processing.GroundedIntelligenceService(retriever, generator, Options.Create(new LlmOptions { MaxContextCharacters = 1000 }), Microsoft.Extensions.Logging.Abstractions.NullLogger<BankingBrain.Infrastructure.Processing.GroundedIntelligenceService>.Instance);
        var answer = await service.AnswerAsync(Guid.NewGuid(), new GroundedQuestion("question"), CancellationToken.None);
        Assert.True(generator.Context!.WasTruncated);
        Assert.Single(answer.Context);
        Assert.NotNull(answer.Context[0].EvidenceId);
    }

    private static SemanticExtractionResult GraphResult(string subject, string middle, string target, DateTime firstDate, DateTime secondDate) => new(
        new[]
        {
            new SemanticEntityCandidate("INSTITUTION", subject, Array.Empty<string>(), .9m, 0),
            new SemanticEntityCandidate("INSTITUTION", middle, Array.Empty<string>(), .9m, 0),
            new SemanticEntityCandidate("FUND", target, Array.Empty<string>(), .9m, 0)
        },
        new[]
        {
            new SemanticClaimCandidate(subject, "OWNS", middle, .8m, 0, "INSTITUTION", "INSTITUTION", AsOfUtc: firstDate, ValidFromUtc: firstDate, ValidToUtc: secondDate),
            new SemanticClaimCandidate(subject, "OWNS", middle, .9m, 0, "INSTITUTION", "INSTITUTION", AsOfUtc: secondDate, ValidFromUtc: secondDate),
            new SemanticClaimCandidate(middle, "OWNS", target, .9m, 0, "INSTITUTION", "FUND", AsOfUtc: secondDate, ValidFromUtc: secondDate)
        },
        new[]
        {
            new SemanticRelationshipCandidate(subject, "OWNS", middle, .9m, 0, "INSTITUTION", "INSTITUTION"),
            new SemanticRelationshipCandidate(middle, "OWNS", target, .9m, 0, "INSTITUTION", "FUND")
        });

    private static SemanticEmbeddingDocument EmbeddingDocument(string content, Guid workspaceId, Guid sourceId, Guid versionId, string marker) => new(
        "DOCUMENT_SECTION", sourceId, versionId, sourceId, null, null, Guid.NewGuid(), content,
        BankingBrain.Infrastructure.Processing.SemanticEmbeddingService.HashContent(content), new Dictionary<string, string> { ["workspace"] = workspaceId.ToString(), ["marker"] = marker });

    private static async Task<string> GetEmbeddingContentHashAsync(IServiceProvider services, Guid embeddingId)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        return (await db.SemanticEmbeddings.SingleAsync(item => item.Id == embeddingId)).ContentHash;
    }

    private sealed class TestEmbeddingProvider : IEmbeddingProvider
    {
        private readonly bool _fail;
        public TestEmbeddingProvider(string identity, string version, string model, bool fail = false) { Identity = identity; Version = version; Model = model; _fail = fail; }
        public string Identity { get; }
        public string Version { get; }
        public string Model { get; }
        public Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
        {
            if (_fail) throw new InvalidOperationException("fake provider failure");
            return Task.FromResult<IReadOnlyList<EmbeddingVector>>(inputs.Select(input => new EmbeddingVector(new[] { input.Contains("replacement", StringComparison.OrdinalIgnoreCase) ? 1f : 0f, input.Contains("bank", StringComparison.OrdinalIgnoreCase) ? 1f : 0f }, Identity, Version, Model)).ToList());
        }
    }

    private sealed class FakeLlmProvider : ILlmGenerationProvider
    {
        private readonly LlmGenerationResult? _result; private readonly bool _fail;
        public FakeLlmProvider(LlmGenerationResult? result, bool fail = false) { _result = result; _fail = fail; }
        public string Identity => "fake"; public string Version => "1"; public string Model => "fake";
        public Task<LlmGenerationResult> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) => _fail ? throw new InvalidOperationException("provider failure") : Task.FromResult(_result!);
    }

    private sealed class FakeContextRetriever : IGroundedContextRetriever
    {
        private readonly GroundedContext _context; public FakeContextRetriever(GroundedContext context) => _context = context;
        public Task<GroundedContext> RetrieveAsync(Guid workspaceId, GroundedQuestion question, Guid correlationId, CancellationToken cancellationToken) => Task.FromResult(_context);
    }

    private sealed class CapturingGenerator : IGroundedAnswerGenerator
    {
        public GroundedContext? Context { get; private set; }
        public Task<GroundedAnswer> GenerateAsync(GroundedContext context, Guid correlationId, CancellationToken cancellationToken) { Context = context; return Task.FromResult(new GroundedAnswer("ok", "grounded", Array.Empty<GroundedCitation>(), context.Items, context.Intent)); }
    }

    private async Task<SemanticSource> CreateSemanticSourceAsync(IServiceProvider services, Fixture fixture)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var section = await db.DocumentSections.SingleAsync(item => item.DocumentVersionId == fixture.VersionId);
        return new SemanticSource(fixture.WorkspaceId, fixture.VersionId, DocumentProcessingState.Processed, new[]
        {
            new NormalizedSection(section.Sequence, section.Kind, section.Title, section.Text, section.PageNumber, section.SourceLocation, DocumentSectionId: section.Id)
        });
    }

    private static async Task PersistResultAsync(
        ISemanticExtractionRepository repository,
        SemanticSource source,
        SemanticExtractionResult result,
        string identity = "test-extractor",
        string version = "1.0")
    {
        var run = await repository.AddRunAsync(SemanticExtractionRun.Start(source.WorkspaceId, source.DocumentVersionId, identity, version), CancellationToken.None);
        await repository.PersistAsync(run, result, source, CancellationToken.None);
    }

    private async Task<Fixture> CreateProcessedVersionAsync(HttpClient client, string text)
    {
        var workspace = await CreateWorkspaceAsync(client);
        var source = await CreateSourceAsync(client, workspace);
        var document = await CreateDocumentAsync(client, workspace, source);
        var versionId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persistedDocument = await db.Documents.SingleAsync(item => item.Id == document);
            var version = DocumentVersion.Create(document, 1, $"semantic-{Guid.NewGuid():N}", "text/plain", "semantic.txt", "documents/semantic", text.Length, "documents/semantic", null);
            version.SetWorkspace(workspace);
            version.BeginProcessing();
            version.MarkProcessed("fixture");
            persistedDocument.SetCurrentVersion(version.Id);
            db.DocumentVersions.Add(version);
            db.DocumentSections.Add(DocumentSection.Create(version.Id, 0, "text", null, text, null, "document"));
            await db.SaveChangesAsync();
            versionId = version.Id;
        }

        return new Fixture(
            workspace,
            versionId,
            $"/api/v1/workspaces/{workspace}/documents/{document}/versions/{versionId}/semantic-extraction",
            $"/api/v1/workspaces/{workspace}/documents/{document}/versions/{versionId}/semantic-extraction/runs",
            $"/api/v1/workspaces/{workspace}/semantic/claims?versionId={versionId}");
    }

    private static async Task<RunListResponse> WaitForSucceededAsync(HttpClient client, string runsUrl)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var runs = await client.GetFromJsonAsync<List<RunListResponse>>(runsUrl);
            var run = runs?.FirstOrDefault();
            if (run?.Status is SemanticExtractionRunStatus.Succeeded or SemanticExtractionRunStatus.Failed)
                return run;
            await Task.Delay(25);
        }
        throw new TimeoutException("Semantic extraction did not reach a terminal state.");
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Users.AnyAsync(user => user.Email == email))
        {
            db.Users.Add(User.Create(email, new PasswordHasher().Hash("Password@123"), email[..email.IndexOf('@')]));
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password@123" });
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static async Task<Guid> CreateWorkspaceAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/workspaces", new { name = $"semantic-{Guid.NewGuid():N}" });
        var value = await response.Content.ReadFromJsonAsync<WorkspaceResponse>();
        return value!.WorkspaceId;
    }

    private static async Task<Guid> CreateSourceAsync(HttpClient client, Guid workspaceId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/knowledge-sources", new { name = $"source-{Guid.NewGuid():N}", sourceType = 6 });
        var value = await response.Content.ReadFromJsonAsync<SourceResponse>();
        return value!.KnowledgeSourceId;
    }

    private static async Task<Guid> CreateDocumentAsync(HttpClient client, Guid workspaceId, Guid sourceId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/documents", new { knowledgeSourceId = sourceId, name = $"document-{Guid.NewGuid():N}", mimeType = "text/plain" });
        var value = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        return value!.DocumentId;
    }

    private sealed record Fixture(Guid WorkspaceId, Guid VersionId, string ExtractionUrl, string RunsUrl, string ClaimsUrl);
    private sealed record LoginResponse(string AccessToken);
    private sealed record WorkspaceResponse(Guid WorkspaceId);
    private sealed record SourceResponse(Guid KnowledgeSourceId);
    private sealed record DocumentResponse(Guid DocumentId);
    private sealed record RunResponse(Guid ExtractionRunId, Guid DocumentVersionId, SemanticExtractionRunStatus Status, int EntityCount, int ClaimCount, int RelationshipCount);
    private sealed record RunListResponse(Guid RunId, Guid DocumentVersionId, SemanticExtractionRunStatus Status, int EntityCount, int ClaimCount, int RelationshipCount);
    private sealed record ClaimResponse(Guid ClaimId, Guid DocumentVersionId, string Subject, string Predicate, string ObjectValue, Guid EvidenceId);
    private sealed record ClaimDetailsResponse(ClaimResponse Claim, EvidenceResponse Evidence);
    private sealed record EvidenceResponse(Guid EvidenceId, Guid DocumentVersionId, Guid? DocumentSectionId, string SourceLocation, string? ExtractedText, string? ExtractedValue);
    private sealed record SemanticVocabularyResponse(Guid Id, string Code, string DisplayName);
    private sealed record SemanticPredicateResponse(Guid Id, string Code, string DisplayName, bool AllowsEntityObject, string AllowedSubjectTypes);
}
