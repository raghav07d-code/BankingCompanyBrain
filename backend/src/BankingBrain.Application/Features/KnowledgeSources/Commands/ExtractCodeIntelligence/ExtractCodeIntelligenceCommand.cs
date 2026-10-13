using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractCodeIntelligence;

public sealed record ExtractCodeIntelligenceCommand(
    Guid WorkspaceId,
    Guid KnowledgeSourceId)
    : ICommand<ExtractCodeIntelligenceResponse>;
