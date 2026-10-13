using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractApiIntelligence;

public sealed record ExtractApiIntelligenceCommand(
    Guid WorkspaceId,
    Guid KnowledgeSourceId)
    : ICommand<ExtractApiIntelligenceResponse>;
