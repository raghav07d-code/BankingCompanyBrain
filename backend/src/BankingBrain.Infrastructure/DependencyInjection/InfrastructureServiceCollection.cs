using BankingBrain.Infrastructure.CodeIntelligence.Projects;
using BankingBrain.Application.Common.CodeIntelligence.Projects;
using BankingBrain.Application.Features.Authentication.Interfaces;
using BankingBrain.Infrastructure.Persistence.Context;
using BankingBrain.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BankingBrain.Infrastructure.Identity.Passwords;
using BankingBrain.Infrastructure.Identity.Jwt;
using BankingBrain.Infrastructure.Identity.Jwt.Configuration;
using Microsoft.Extensions.Options;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using BankingBrain.Infrastructure.Identity.Services;
using BankingBrain.Infrastructure.Persistence.Seed;
using BankingBrain.Infrastructure.Identity.CurrentUser;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Infrastructure.Processing;
using BankingBrain.Infrastructure.Storage;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Application.Features.Semantics.Queries;
using BankingBrain.Application.Common.Embeddings;
using BankingBrain.Application.Common.Graph;
using BankingBrain.Application.Common.GroundedIntelligence;
using BankingBrain.Infrastructure.Configuration;
using BankingBrain.Application.Common.Connectors;
using BankingBrain.Infrastructure.Connectors;
using BankingBrain.Infrastructure.Connectors.FileSystem;
using BankingBrain.Infrastructure.Connectors.Git;
using BankingBrain.Infrastructure.Configuration.Connectors;
using BankingBrain.Application.Common.Connectors.Reconciliation;

using BankingBrain.Application.Common.CodeIntelligence;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Infrastructure.CodeIntelligence;
using BankingBrain.Infrastructure.CodeIntelligence.Semantics;
using BankingBrain.Application.Common.SqlLineage;
using BankingBrain.Infrastructure.SqlLineage;
using BankingBrain.Application.Common.ApiIntelligence;
using BankingBrain.Infrastructure.ApiIntelligence;
using BankingBrain.Application.Common.DataAccessIntelligence;
using BankingBrain.Infrastructure.DataAccessIntelligence;

namespace BankingBrain.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"));
        });
        services.Configure<JwtOptions>(
        configuration.GetSection(JwtOptions.SectionName));
        services.Configure<ProcessingOptions>(
            configuration.GetSection(ProcessingOptions.SectionName));
        var jwt = configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()!;

    services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,

                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwt.Secret)),

                    ClockSkew = TimeSpan.Zero
                };
        });

    services.AddAuthorization();

        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IKnowledgeSourceRepository, KnowledgeSourceRepository>();
        services.AddScoped<IKnowledgeSourceRefreshRunRepository, KnowledgeSourceRefreshRunRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentVersionRepository, DocumentVersionRepository>();
        services.AddScoped<IProcessingJobRepository, ProcessingJobRepository>();
        services.AddScoped<ISemanticExtractionRepository, SemanticExtractionRepository>();
        services.AddScoped<ISemanticQueryService, SemanticQueryService>();
        services.AddScoped<ISemanticEmbeddingStore, SemanticEmbeddingStore>();
        services.AddScoped<ISemanticEmbeddingService, SemanticEmbeddingService>();
        services.AddScoped<ISemanticGraphService, SemanticGraphService>();
        services.AddScoped<IQuestionIntentAnalyzer, DeterministicQuestionIntentAnalyzer>();
        services.AddScoped<IGroundedContextRetriever, SemanticGroundedContextRetriever>();
        services.AddScoped<IGroundedAnswerGenerator, DeterministicGroundedAnswerGenerator>();
        services.AddScoped<IGroundedAnswerGenerator, ConfiguredGroundedAnswerGenerator>();
        services.AddScoped<IGroundedIntelligenceService, GroundedIntelligenceService>();
        services.AddScoped<ISemanticProcessingJobRepository, SemanticProcessingJobRepository>();
        services.AddScoped<IBankingSemanticVocabulary, BankingSemanticVocabulary>();
        services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        services.AddSingleton<IDocumentUploadPolicy, DocumentUploadPolicy>();
        services.AddSingleton<IDocumentProcessor, TextDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, PdfDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, DocxDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, XlsxDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, PptxDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, OcrDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, SqlDocumentProcessor>();
        services.AddSingleton<ISemanticExtractor, DeterministicSemanticExtractor>();
        services.AddSingleton<IEmbeddingProvider, DeterministicEmbeddingProvider>();
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<ConnectorOptions>(configuration.GetSection(ConnectorOptions.SectionName));
        services.AddSingleton<ConnectorPathPolicy>();
        services.AddSingleton<IImmutableRepositorySnapshotWriter, ImmutableRepositorySnapshotWriter>();
        services.AddHttpClient<HttpLlmGenerationProvider>();
        services.AddScoped<ILlmGenerationProvider>(services => services.GetRequiredService<HttpLlmGenerationProvider>());
        services.AddHostedService<DocumentProcessingWorker>();
        services.AddHostedService<SemanticExtractionWorker>();
        services.AddSingleton<ICSharpCodeParser, RoslynCSharpCodeParser>();
        services.AddSingleton<ICodeProjectDiscovery, DotNetProjectDiscovery>();
        services.AddSingleton<ICodeCompilationBuilder, DotNetCompilationBuilder>();
        services.AddSingleton<ICodeRepositoryAnalyzer, RoslynCodeRepositoryAnalyzer>();
        services.AddScoped<ICodeRepositorySemanticExtractionService, CodeRepositorySemanticExtractionService>();
        services.AddSingleton<InMemoryCodeIntelligenceRefreshQueue>();
        services.AddSingleton<ICodeIntelligenceRefreshQueue>(
            sp => sp.GetRequiredService<InMemoryCodeIntelligenceRefreshQueue>());
        services.AddHostedService<CodeIntelligenceRefreshWorker>();
        services.AddScoped<ISqlLineageExtractionService, SqlLineageExtractionService>();
        services.AddSingleton<InMemorySqlLineageRefreshQueue>();
        services.AddSingleton<ISqlLineageRefreshQueue>(
            sp => sp.GetRequiredService<InMemorySqlLineageRefreshQueue>());
        services.AddHostedService<SqlLineageRefreshWorker>();
        services.AddSingleton<IApiEndpointDiscoveryAnalyzer, ApiEndpointDiscoveryAnalyzer>();
        services.AddScoped<IApiIntelligenceExtractionService, ApiIntelligenceExtractionService>();
        services.AddSingleton<InMemoryApiIntelligenceRefreshQueue>();
        services.AddSingleton<IApiIntelligenceRefreshQueue>(
            sp => sp.GetRequiredService<InMemoryApiIntelligenceRefreshQueue>());
        services.AddHostedService<ApiIntelligenceRefreshWorker>();
        services.AddSingleton<IEfDataAccessAnalyzer, EfDataAccessAnalyzer>();
        services.AddScoped<IDataAccessIntelligenceExtractionService, DataAccessIntelligenceExtractionService>();
        services.AddSingleton<ISourceConnectorRegistry, SourceConnectorRegistry>();
        services.AddSingleton<ISourceConnector, FileSystemSourceConnector>();
        services.AddSingleton<ISourceConnector, GitSourceConnector>();
        services.AddScoped<ISourceConnectorService, SourceConnectorService>();
        services.AddScoped<KnowledgeSourceSnapshotService>();
services.AddScoped<GitSnapshotReconciliationService>();
        return services;
    }
}
