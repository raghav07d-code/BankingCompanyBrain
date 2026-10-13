using System.Diagnostics;
using System.Text.RegularExpressions;
using BankingBrain.Application.Common.GroundedIntelligence;
using BankingBrain.Application.Common.Embeddings;
using BankingBrain.Application.Common.Graph;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Application.Features.Semantics.Queries;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using BankingBrain.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BankingBrain.Infrastructure.Processing;

public sealed class DeterministicQuestionIntentAnalyzer : IQuestionIntentAnalyzer
{
    /// <summary>
    /// Fixed, code-shaped keyword set used purely as a cheap pre-filter before attempting
    /// exact code-entity resolution (mirrors the banking predicate/keyword-match approach
    /// below, but code predicates/entity types are a fixed vocabulary produced by Code
    /// Intelligence, not a per-workspace DB table).
    /// </summary>
    private static readonly string[] CodeKeywords =
    [
        "CLASS", "INTERFACE", "STRUCT", "RECORD", "ENUM", "METHOD", "PROPERTY", "FIELD",
        "NAMESPACE", "PROJECT", "REPOSITORY", "CALLS", "CALLER", "CALLERS", "CALLED",
        "INHERITS", "INHERITANCE", "IMPLEMENTS", "IMPLEMENTATION", "IMPLEMENTATIONS",
        "DEPENDS", "DEPENDENCY", "DEPENDENCIES", "REFERENCES", "CONTAINS", "MEMBERS", "CODE",
        // SQL Lineage vocabulary - same cheap pre-filter role as the C#-shaped keywords
        // above, so a question like "what could break if this column changes" engages
        // the same exact-match entity resolution + provenance path as a code question.
        "TABLE", "TABLES", "COLUMN", "COLUMNS", "VIEW", "VIEWS", "SCHEMA", "SCHEMAS",
        "DATABASE", "DATABASES", "PROCEDURE", "PROCEDURES", "FUNCTION", "FUNCTIONS",
        "WRITES", "WRITE", "READS", "READ", "DERIVE", "DERIVES", "DERIVED", "BREAK", "BREAKS", "SQL",
        // API Intelligence vocabulary.
        "ENDPOINT", "ENDPOINTS", "API", "ROUTE", "ROUTES", "HANDLER", "HANDLERS", "CONTROLLER", "CONTROLLERS"
    ];

    private static readonly HashSet<string> CodeEntityTypes = new(StringComparer.Ordinal)
    {
        "Repository", "Project", "Namespace", "Class", "Interface", "Struct", "Record",
        "Enum", "Method", "Property", "Field",
        "SqlDatabase", "SqlSchema", "SqlTable", "SqlColumn", "SqlView", "SqlProcedure", "SqlFunction",
        "ApiEndpoint"
    };

    private static readonly Regex IdentifierPattern = new(
        @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*",
        RegexOptions.Compiled);

    private readonly IBankingSemanticVocabulary _vocabulary;
    private readonly ISemanticQueryService _semantic;
    private readonly ILogger<DeterministicQuestionIntentAnalyzer> _logger;

    public DeterministicQuestionIntentAnalyzer(IBankingSemanticVocabulary vocabulary, ISemanticQueryService semantic, ILogger<DeterministicQuestionIntentAnalyzer> logger)
    {
        _vocabulary = vocabulary;
        _semantic = semantic;
        _logger = logger;
    }

    public async Task<QuestionIntent> AnalyzeAsync(Guid workspaceId, GroundedQuestion question, Guid correlationId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var predicates = await _vocabulary.GetPredicatesAsync(workspaceId, cancellationToken);
        var normalized = question.Question.ToUpperInvariant();
        var match = predicates.FirstOrDefault(predicate => normalized.Contains(predicate.Code) || normalized.Contains(predicate.DisplayName.ToUpperInvariant()));

        string? codeEntityName = null;

        if (CodeKeywords.Any(keyword => normalized.Contains(keyword)))
        {
            codeEntityName = await ResolveCodeEntityCandidateAsync(workspaceId, question.Question, correlationId, cancellationToken);
        }

        var intent = new QuestionIntent(
            match?.Code,
            codeEntityName,
            match is not null,
            match?.AllowsEntityObject == true,
            true,
            codeEntityName is not null);

        // Never logs question text or any retrieved content - stage name, workspace,
        // elapsed time, and coarse outcome flags only.
        _logger.LogInformation(
            "GroundedStageTiming Stage={Stage} CorrelationId={CorrelationId} WorkspaceId={WorkspaceId} ElapsedMs={ElapsedMs}",
            "IntentAnalysis", correlationId, workspaceId, stopwatch.Elapsed.TotalMilliseconds);

        return intent;
    }

    /// <summary>
    /// Scans the question for identifier-shaped substrings and accepts the first one that
    /// is an EXACT CanonicalName/ExternalIdentifier match against an existing code entity
    /// in this workspace. Never guesses or fuzzy-matches: an unmatched candidate is simply
    /// discarded, so a question with no real code-entity reference yields no code context.
    /// </summary>
    private async Task<string?> ResolveCodeEntityCandidateAsync(Guid workspaceId, string questionText, Guid correlationId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var resolved = await ResolveCodeEntityCandidateCoreAsync(workspaceId, questionText, cancellationToken);

        _logger.LogInformation(
            "GroundedStageTiming Stage={Stage} CorrelationId={CorrelationId} WorkspaceId={WorkspaceId} ElapsedMs={ElapsedMs} Resolved={Resolved}",
            "EntityResolution", correlationId, workspaceId, stopwatch.Elapsed.TotalMilliseconds, resolved is not null);

        return resolved;
    }

    private async Task<string?> ResolveCodeEntityCandidateCoreAsync(Guid workspaceId, string questionText, CancellationToken cancellationToken)
    {
        var candidates = IdentifierPattern.Matches(questionText)
            .Select(match => match.Value)
            .Where(value => value.Length > 1)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length)
            .ThenBy(value => value, StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            // Pass the known Code Intelligence entity-type vocabulary up front, so the
            // server-side query narrows to those types instead of materializing every
            // entity type in the workspace only to discard non-code ones afterward.
            var entity = await _semantic.FindCodeEntityAsync(workspaceId, null, candidate, null, CodeEntityTypes, cancellationToken);

            if (entity is not null && CodeEntityTypes.Contains(entity.EntityType))
            {
                return entity.CanonicalName;
            }
        }

        return null;
    }
}

public sealed class SemanticGroundedContextRetriever : IGroundedContextRetriever
{
    private readonly IQuestionIntentAnalyzer _analyzer;
    private readonly ISemanticQueryService _semantic;
    private readonly ISemanticEmbeddingService _embeddings;
    private readonly ISemanticGraphService _graph;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SemanticGroundedContextRetriever> _logger;

    public SemanticGroundedContextRetriever(IQuestionIntentAnalyzer analyzer, ISemanticQueryService semantic, ISemanticEmbeddingService embeddings, ISemanticGraphService graph, ApplicationDbContext db, ILogger<SemanticGroundedContextRetriever> logger) { _analyzer = analyzer; _semantic = semantic; _embeddings = embeddings; _graph = graph; _db = db; _logger = logger; }

    public async Task<GroundedContext> RetrieveAsync(Guid workspaceId, GroundedQuestion question, Guid correlationId, CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var intent = await _analyzer.AnalyzeAsync(workspaceId, question, correlationId, cancellationToken);
        var items = new Dictionary<string, GroundedContextItem>(StringComparer.Ordinal);
        if (intent.RequiresExactClaims)
        {
            var stageStopwatch = Stopwatch.StartNew();
            var exact = await _semantic.QueryClaimsAsync(workspaceId, new SemanticClaimQuery(Predicate: intent.Predicate, AsOfUtc: question.AsOfUtc, ValidAtUtc: question.ValidAtUtc), cancellationToken);
            foreach (var claim in exact.Claims)
            {
                var details = await _semantic.GetEvidenceAsync(workspaceId, claim.Id, cancellationToken);
                if (details is not null) Add(items, new GroundedContextItem("claim", $"{claim.Subject} {claim.Predicate} {claim.ObjectValue}", claim.DocumentVersionId, details.Evidence.DocumentSectionId, claim.SubjectEntityId, claim.Id, null, details.Evidence.Id, details.Evidence.SourceLocation, claim.ValidFromUtc, claim.ValidToUtc, claim.AsOfUtc, claim.ExtractionConfidence, claim.SourceAuthorityScore));
            }
            LogStage("ExactClaimsRetrieval", workspaceId, correlationId, stageStopwatch, items.Count);
        }
        if (intent.RequiresSemanticSearch)
        {
            var stageStopwatch = Stopwatch.StartNew();
            var vectorResults = await _embeddings.SearchAsync(workspaceId, new SemanticEmbeddingSearchRequest(question.Question, 20), cancellationToken);
            foreach (var result in vectorResults)
            {
                if (result.SemanticClaimId is Guid claimId)
                {
                    var details = await _semantic.GetEvidenceAsync(workspaceId, claimId, cancellationToken);
                    if (details is not null && MatchesTime(details.Claim.ValidFromUtc, details.Claim.ValidToUtc, details.Claim.AsOfUtc, question)) Add(items, new GroundedContextItem("claim", $"{details.Claim.Subject} {details.Claim.Predicate} {details.Claim.ObjectValue}", details.Claim.DocumentVersionId, details.Evidence.DocumentSectionId, details.Claim.SubjectEntityId, details.Claim.Id, null, details.Evidence.Id, details.Evidence.SourceLocation, details.Claim.ValidFromUtc, details.Claim.ValidToUtc, details.Claim.AsOfUtc, details.Claim.ExtractionConfidence, details.Claim.SourceAuthorityScore));
                }
                else Add(items, new GroundedContextItem("semantic", result.SourceType, result.DocumentVersionId, result.DocumentSectionId, result.SemanticEntityId, null, null, result.EvidenceId, result.Metadata.GetValueOrDefault("source.location"), null, null, null, null, null));
            }
            LogStage("SemanticRetrieval", workspaceId, correlationId, stageStopwatch, items.Count);
        }
        if (intent.RequiresGraph)
        {
            var stageStopwatch = Stopwatch.StartNew();
            var graphResult = await _graph.QueryAsync(workspaceId, new SemanticGraphQuery(Predicate: intent.Predicate, AsOfUtc: question.AsOfUtc, ValidAtUtc: question.ValidAtUtc, MaxDepth: question.MaxGraphDepth), cancellationToken);
            foreach (var edge in graphResult.Edges)
            {
                foreach (var claimId in edge.SupportingClaimIds)
                {
                    var details = await _semantic.GetEvidenceAsync(workspaceId, claimId, cancellationToken);
                    if (details is not null) Add(items, new GroundedContextItem("relationship", $"{edge.SubjectEntityId} {edge.Predicate} {edge.ObjectEntityId}", edge.DocumentVersionId, details.Evidence.DocumentSectionId, edge.SubjectEntityId, claimId, edge.SourceRelationshipId, details.Evidence.Id, details.Evidence.SourceLocation, edge.ValidFromUtc, edge.ValidToUtc, edge.AsOfUtc, edge.ExtractionConfidence, edge.SourceAuthorityScore));
                }
            }
            LogStage("RelationshipRetrieval", workspaceId, correlationId, stageStopwatch, items.Count);
        }
        if (intent.RequiresCodeSearch && intent.EntityName is not null)
        {
            var stageStopwatch = Stopwatch.StartNew();
            var entity = await _semantic.FindCodeEntityAsync(workspaceId, null, intent.EntityName, null, cancellationToken);
            if (entity is not null)
            {
                var provenance = await _semantic.GetEntityProvenanceAsync(workspaceId, entity.EntityId, cancellationToken);
                foreach (var detail in provenance)
                {
                    var subjectName = detail.SubjectEntity?.CanonicalName ?? detail.Relationship.SubjectEntityId.ToString();
                    var objectName = detail.ObjectEntity?.CanonicalName ?? detail.Relationship.ObjectEntityId.ToString();
                    Add(items, new GroundedContextItem("code", $"{subjectName} {detail.Relationship.RelationType} {objectName}", detail.Relationship.DocumentVersionId, detail.Evidence.DocumentSectionId, entity.EntityId, null, detail.Relationship.Id, detail.Evidence.Id, detail.Evidence.SourceLocation, null, null, null, detail.Relationship.ExtractionConfidence, null));
                }
            }
            LogStage("ProvenanceRetrieval", workspaceId, correlationId, stageStopwatch, items.Count);
        }
        var context = new GroundedContext(items.Values.ToList(), intent);
        LogStage("GroundedContextConstruction", workspaceId, correlationId, totalStopwatch, context.Items.Count);
        return context;
    }

    private void LogStage(string stage, Guid workspaceId, Guid correlationId, Stopwatch stopwatch, int itemCount) =>
        _logger.LogInformation(
            "GroundedStageTiming Stage={Stage} CorrelationId={CorrelationId} WorkspaceId={WorkspaceId} ElapsedMs={ElapsedMs} ItemCount={ItemCount}",
            stage, correlationId, workspaceId, stopwatch.Elapsed.TotalMilliseconds, itemCount);

    private static void Add(IDictionary<string, GroundedContextItem> items, GroundedContextItem item) => items.TryAdd($"{item.Kind}|{item.SemanticClaimId}|{item.EvidenceId}|{item.RelationshipId}|{item.DocumentSectionId}", item);
    private static bool MatchesTime(DateTime? from, DateTime? to, DateTime? asOf, GroundedQuestion question) => (question.AsOfUtc is null || asOf is null || asOf <= question.AsOfUtc) && (question.ValidAtUtc is null || (from is null || from <= question.ValidAtUtc) && (to is null || to >= question.ValidAtUtc));
}

public sealed class DeterministicGroundedAnswerGenerator : IGroundedAnswerGenerator
{
    public Task<GroundedAnswer> GenerateAsync(GroundedContext context, Guid correlationId, CancellationToken cancellationToken)
    {
        if (context.Items.Count == 0) return Task.FromResult(new GroundedAnswer("Insufficient evidence to answer the question.", "insufficient-evidence", Array.Empty<GroundedCitation>(), context.Items, context.Intent));
        var conflicts = context.Items.Where(item => item.SemanticClaimId is not null).GroupBy(item => $"{item.Kind}|{item.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}|{context.Intent.Predicate}").Any(group => group.Select(item => item.Text).Distinct().Count() > 1);
        var answer = conflicts ? "The available evidence contains conflicting observations: " + string.Join("; ", context.Items.Select(item => item.Text).Distinct()) : string.Join("; ", context.Items.Select(item => item.Text).Distinct());
        var citations = context.Items.Select(item => new GroundedCitation(item.EvidenceId, item.DocumentVersionId, item.DocumentSectionId, item.SourceLocation, item.SemanticClaimId, item.RelationshipId)).Where(citation => citation.EvidenceId is not null || citation.SemanticClaimId is not null || citation.RelationshipId is not null).Distinct().ToList();
        return Task.FromResult(new GroundedAnswer(answer, conflicts ? "conflicting-evidence" : "grounded", citations, context.Items, context.Intent));
    }
}

public sealed class GroundedIntelligenceService : IGroundedIntelligenceService
{
    private readonly IGroundedContextRetriever _retriever;
    private readonly IGroundedAnswerGenerator _generator;
    private readonly LlmOptions _options;
    private readonly ILogger<GroundedIntelligenceService> _logger;
    public GroundedIntelligenceService(IGroundedContextRetriever retriever, IGroundedAnswerGenerator generator, IOptions<LlmOptions> options, ILogger<GroundedIntelligenceService> logger) { _retriever = retriever; _generator = generator; _options = options.Value; _logger = logger; }
    public async Task<GroundedAnswer> AnswerAsync(Guid workspaceId, GroundedQuestion question, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var totalStopwatch = Stopwatch.StartNew();

        // Diagnostic-only: a direct ThreadPool snapshot (never customer content) at request
        // start, to confirm or rule out thread-pool starvation as a cause of request-level
        // latency without having to infer it from elapsed-time gaps alone.
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableIoThreads);
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var minIoThreads);

        // Request-start marker: never logs question text - stage name, correlation id,
        // and workspace only, so it can be joined against the stage timings that follow.
        _logger.LogInformation(
            "GroundedStageTiming Stage={Stage} CorrelationId={CorrelationId} WorkspaceId={WorkspaceId} AvailableWorkerThreads={AvailableWorkerThreads} MinWorkerThreads={MinWorkerThreads} AvailableIoThreads={AvailableIoThreads} MinIoThreads={MinIoThreads} PendingWorkItems={PendingWorkItems}",
            "RequestStart", correlationId, workspaceId, availableWorkerThreads, minWorkerThreads, availableIoThreads, minIoThreads, ThreadPool.PendingWorkItemCount);

        var retrievalStopwatch = Stopwatch.StartNew();
        var context = await _retriever.RetrieveAsync(workspaceId, question, correlationId, cancellationToken);
        var retrievalElapsedMs = retrievalStopwatch.Elapsed.TotalMilliseconds;

        var limit = Math.Clamp(_options.MaxContextCharacters, 1000, 100000);
        var total = 0; var kept = new List<GroundedContextItem>();
        foreach (var item in context.Items)
        {
            var size = item.Text.Length + 160;
            if (kept.Count > 0 && total + size > limit) break;
            kept.Add(item); total += size;
        }
        var bounded = context with { Items = kept, WasTruncated = kept.Count < context.Items.Count, CharacterCount = total };

        var generationStopwatch = Stopwatch.StartNew();
        var answer = await _generator.GenerateAsync(bounded, correlationId, cancellationToken);
        var generationElapsedMs = generationStopwatch.Elapsed.TotalMilliseconds;

        var resolvedEntityId = bounded.Items.Select(item => item.SemanticEntityId).FirstOrDefault(id => id is not null);
        var outcome = answer.EvidenceStatus switch
        {
            "grounded" or "conflicting-evidence" => "grounded",
            "insufficient-evidence" => "insufficient-evidence",
            "provider-failure" => "provider-failure",
            _ => "other"
        };

        // Never logs the generated answer text, prompts, or evidence content - only
        // coarse counts/identifiers and timing suitable for downstream metrics ingestion.
        _logger.LogInformation(
            "GroundedStageTiming Stage={Stage} CorrelationId={CorrelationId} WorkspaceId={WorkspaceId} ResolvedEntityId={ResolvedEntityId} RetrievalMs={RetrievalMs} GenerationMs={GenerationMs} TotalMs={TotalMs} EvidenceStatus={EvidenceStatus} Outcome={Outcome} ItemCount={ItemCount} CitationCount={CitationCount} ProviderIdentity={ProviderIdentity} Model={Model}",
            "TotalRequest", correlationId, workspaceId, resolvedEntityId, retrievalElapsedMs, generationElapsedMs, totalStopwatch.Elapsed.TotalMilliseconds, answer.EvidenceStatus, outcome, bounded.Items.Count, answer.Citations.Count, answer.ProviderIdentity, answer.Model);

        return answer;
    }
}