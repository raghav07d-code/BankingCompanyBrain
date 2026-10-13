using BankingBrain.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using BankingBrain.Domain.Entities.Workspace;
using BankingBrain.Domain.Entities.KnowledgeSource;
using BankingBrain.Domain.Entities.Document;
using BankingBrain.Domain.Entities.DocumentVersion;
using BankingBrain.Domain.Entities.DocumentSection;
using BankingBrain.Domain.Entities.ProcessingAttempt;
using BankingBrain.Domain.Entities.ProvenanceAnchor;
using BankingBrain.Domain.Entities.ProcessingJob;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Domain.Entities.Refresh;

namespace BankingBrain.Infrastructure.Persistence.Context;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<User> Users => Set<User>();

    public DbSet<KnowledgeSource> KnowledgeSources => Set<KnowledgeSource>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentSection> DocumentSections => Set<DocumentSection>();

    public DbSet<ProcessingAttempt> ProcessingAttempts => Set<ProcessingAttempt>();

    public DbSet<ProvenanceAnchor> ProvenanceAnchors => Set<ProvenanceAnchor>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<SemanticEntity> SemanticEntities => Set<SemanticEntity>();
    public DbSet<SemanticEvidence> SemanticEvidence => Set<SemanticEvidence>();
    public DbSet<SemanticClaim> SemanticClaims => Set<SemanticClaim>();
    public DbSet<SemanticEmbedding> SemanticEmbeddings => Set<SemanticEmbedding>();
    public DbSet<SemanticGraphNode> SemanticGraphNodes => Set<SemanticGraphNode>();
    public DbSet<SemanticGraphEdge> SemanticGraphEdges => Set<SemanticGraphEdge>();
    public DbSet<SemanticRelationship> SemanticRelationships => Set<SemanticRelationship>();
    public DbSet<SemanticExtractionRun> SemanticExtractionRuns => Set<SemanticExtractionRun>();
    public DbSet<SemanticProcessingJob> SemanticProcessingJobs => Set<SemanticProcessingJob>();
    public DbSet<SemanticEntityType> SemanticEntityTypes => Set<SemanticEntityType>();
    public DbSet<SemanticPredicate> SemanticPredicates => Set<SemanticPredicate>();
    public DbSet<KnowledgeSourceRefreshRun> KnowledgeSourceRefreshRuns => Set<KnowledgeSourceRefreshRun>();

    protected override void OnModelCreating(
    ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly);
    }
}