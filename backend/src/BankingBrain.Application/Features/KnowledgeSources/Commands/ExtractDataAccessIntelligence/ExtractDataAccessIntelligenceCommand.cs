using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractDataAccessIntelligence;

public sealed record ExtractDataAccessIntelligenceCommand(
    Guid WorkspaceId,
    Guid KnowledgeSourceId)
    : ICommand<ExtractDataAccessIntelligenceResponse>;
