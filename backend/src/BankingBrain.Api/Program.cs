using BankingBrain.Application.DependencyInjection;
using BankingBrain.Infrastructure.DependencyInjection;
using BankingBrain.Api.Middleware;
using BankingBrain.Api.Endpoints.Authentication;
using BankingBrain.Application.Features.Authentication.Commands.Login;
using BankingBrain.Infrastructure.Persistence.Seed;
using BankingBrain.Api.Endpoints.Users;
using BankingBrain.Api.Endpoints.Workspaces;
using BankingBrain.Api.Endpoints.KnowledgeSources;
using BankingBrain.Api.Endpoints.Documents;
using Scalar.AspNetCore;
using BankingBrain.Api.Endpoints.Semantics;


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<
        BankingBrain.Api.OpenApi.BearerSecuritySchemeTransformer>();
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// app.UseHttpsRedirection();

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapGetCurrentUser();
app.MapCreateWorkspace();
app.MapGetMyWorkspaces();
app.MapGetWorkspace();
app.MapCreateKnowledgeSource();
app.MapGetKnowledgeSources();
app.MapGetKnowledgeSource();
app.MapSnapshotKnowledgeSource();
app.MapExtractCodeIntelligence();
app.MapExtractSqlLineage();
app.MapExtractApiIntelligence();
app.MapExtractDataAccessIntelligence();
app.MapGetKnowledgeSourceRefreshRun();
app.MapCreateDocument();
app.MapGetDocuments();
app.MapGetDocument();
app.MapIngestDocument();
app.MapReprocessDocumentVersion();
app.MapGetDocumentVersions();
app.MapGetDocumentVersion();
app.MapGetDocumentVersionContent();
app.MapSemanticEndpoints();
app.MapLoginEndpoint();
await app.SeedDatabaseAsync();
app.Run();

public partial class Program
{
}
