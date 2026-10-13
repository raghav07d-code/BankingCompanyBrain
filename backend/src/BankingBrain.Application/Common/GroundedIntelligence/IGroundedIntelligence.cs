namespace BankingBrain.Application.Common.GroundedIntelligence;

public interface IQuestionIntentAnalyzer
{
    Task<QuestionIntent> AnalyzeAsync(Guid workspaceId, GroundedQuestion question, Guid correlationId, CancellationToken cancellationToken);
}

public interface IGroundedContextRetriever
{
    Task<GroundedContext> RetrieveAsync(Guid workspaceId, GroundedQuestion question, Guid correlationId, CancellationToken cancellationToken);
}

public interface IGroundedAnswerGenerator
{
    Task<GroundedAnswer> GenerateAsync(GroundedContext context, Guid correlationId, CancellationToken cancellationToken);
}

public sealed record LlmGenerationResult(string AnswerText, string EvidenceStatus, IReadOnlyList<string> CitationReferences, string ProviderIdentity, string ProviderVersion, string Model);

public interface ILlmGenerationProvider
{
    string Identity { get; }
    string Version { get; }
    string Model { get; }
    Task<LlmGenerationResult> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

public interface IGroundedIntelligenceService
{
    Task<GroundedAnswer> AnswerAsync(Guid workspaceId, GroundedQuestion question, CancellationToken cancellationToken);
}