using System.Diagnostics;
using System.Text;
using BankingBrain.Application.Common.GroundedIntelligence;
using BankingBrain.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BankingBrain.Infrastructure.Processing;

public sealed class ConfiguredGroundedAnswerGenerator : IGroundedAnswerGenerator
{
    private readonly ILlmGenerationProvider _provider;
    private readonly LlmOptions _options;
    private readonly ILogger<ConfiguredGroundedAnswerGenerator> _logger;
    private readonly DeterministicGroundedAnswerGenerator _fallback = new();

    public ConfiguredGroundedAnswerGenerator(ILlmGenerationProvider provider, IOptions<LlmOptions> options, ILogger<ConfiguredGroundedAnswerGenerator> logger) { _provider = provider; _options = options.Value; _logger = logger; }

    public async Task<GroundedAnswer> GenerateAsync(GroundedContext context, Guid correlationId, CancellationToken cancellationToken)
    {
        if (context.Items.Count == 0 || !string.Equals(_options.Provider, "http", StringComparison.OrdinalIgnoreCase)) return await _fallback.GenerateAsync(context, correlationId, cancellationToken);
        var systemPrompt =
            "You are a grounded question-answering assistant. Answer only using the evidence-backed " +
            "context supplied in the user message; never invent facts, sources, or citation references " +
            "not present in that context. Distinguish established facts from uncertainty, and surface " +
            "conflicting observations rather than silently picking one. " +
            "Each context line begins with a \"[cite: ...]\" tag listing the exact citation reference " +
            "token(s) that line may be cited under (each shaped like \"claim:<guid>\", " +
            "\"evidence:<guid>\", or \"relationship:<guid>\"); a line tagged \"[cite: none]\" carries no " +
            "citable reference and must not be cited. " +
            "Respond with a single JSON object with exactly three fields: \"answer\" (the answer text), " +
            "\"evidenceStatus\" (one of \"grounded\", \"conflicting-evidence\", or \"insufficient-evidence\" " +
            "- use \"insufficient-evidence\" if the supplied context does not answer the question), and " +
            "\"citations\" (a JSON array of citation reference tokens copied verbatim from the \"[cite: ...]\" " +
            "tags of context lines you actually relied on; use an empty array if none apply, and never " +
            "include a token that was not supplied in one of those tags).";
        var userPrompt = BuildContextPrompt(context);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var generated = await _provider.GenerateAsync(systemPrompt, userPrompt, cancellationToken);
            var citations = MapCitations(generated.CitationReferences, context);
            var status = generated.EvidenceStatus is "grounded" or "conflicting-evidence" or "insufficient-evidence" ? generated.EvidenceStatus : "grounded";

            LogLlmStage(correlationId, stopwatch, status, null);
            return new GroundedAnswer(generated.AnswerText, status, citations, context.Items, context.Intent, generated.ProviderIdentity, generated.ProviderVersion, generated.Model);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Never log systemPrompt/userPrompt (they contain grounded context and could be
            // large/sensitive) or any provider request header - only the exception itself,
            // whose message from HttpLlmGenerationProvider never contains the API key.
            _logger.LogWarning(exception, "LLM provider {ProviderIdentity} timed out. CorrelationId={CorrelationId}", _provider.Identity, correlationId);
            LogLlmStage(correlationId, stopwatch, "provider-failure", null);
            return new GroundedAnswer("The language provider timed out.", "provider-failure", Array.Empty<GroundedCitation>(), context.Items, context.Intent, _provider.Identity, _provider.Version, _provider.Model);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "LLM provider {ProviderIdentity} call failed. CorrelationId={CorrelationId}", _provider.Identity, correlationId);
            LogLlmStage(correlationId, stopwatch, "provider-failure", (exception as HttpRequestException)?.StatusCode is { } statusCode ? (int)statusCode : null);
            return new GroundedAnswer("The language provider was unavailable.", "provider-failure", Array.Empty<GroundedCitation>(), context.Items, context.Intent, _provider.Identity, _provider.Version, _provider.Model);
        }
    }

    private void LogLlmStage(Guid correlationId, Stopwatch stopwatch, string outcome, int? providerStatusCode) =>
        // LLM generation duration is only recorded here, guarded by the "actually invoked
        // the HTTP provider" branch above - the deterministic fallback path never reaches
        // this method, so there is no duplicate/overlapping timing with GroundedStageTiming
        // TotalRequest's GenerationMs (which covers this whole call plus fallback cases).
        _logger.LogInformation(
            "GroundedStageTiming Stage={Stage} CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} Outcome={Outcome} ProviderIdentity={ProviderIdentity} Model={Model} ProviderStatusCode={ProviderStatusCode}",
            "LlmGeneration", correlationId, stopwatch.Elapsed.TotalMilliseconds, outcome, _provider.Identity, _provider.Model, providerStatusCode);

    private static string BuildContextPrompt(GroundedContext context)
    {
        var builder = new StringBuilder("Question context (each line's citable reference token(s), if any, are in its [cite: ...] tag):\n");

        foreach (var item in context.Items)
        {
            var tokens = new List<string>();

            if (item.EvidenceId is Guid evidenceId) tokens.Add($"evidence:{evidenceId}");
            if (item.SemanticClaimId is Guid claimId) tokens.Add($"claim:{claimId}");
            if (item.RelationshipId is Guid relationshipId) tokens.Add($"relationship:{relationshipId}");

            var cite = tokens.Count > 0 ? string.Join(", ", tokens) : "none";

            builder.Append("[cite: ").Append(cite).Append("] ").AppendLine(item.Text);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<GroundedCitation> MapCitations(IEnumerable<string> references, GroundedContext context)
    {
        var result = new List<GroundedCitation>();
        foreach (var reference in references ?? Array.Empty<string>())
        {
            var parts = reference.Split(':', 2);
            if (parts.Length != 2 || !Guid.TryParse(parts[1], out var id)) continue;
            var item = parts[0].ToLowerInvariant() switch
            {
                "claim" => context.Items.FirstOrDefault(value => value.SemanticClaimId == id),
                "evidence" => context.Items.FirstOrDefault(value => value.EvidenceId == id),
                "relationship" => context.Items.FirstOrDefault(value => value.RelationshipId == id),
                _ => null
            };
            if (item is not null) result.Add(new GroundedCitation(item.EvidenceId, item.DocumentVersionId, item.DocumentSectionId, item.SourceLocation, item.SemanticClaimId, item.RelationshipId));
        }
        return result.Distinct().ToList();
    }
}