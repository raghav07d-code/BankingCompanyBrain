using BankingBrain.Application.Common.Results;
using MediatR;

namespace BankingBrain.Application.Common.Connectors.SnapshotKnowledgeSource;

public sealed record SnapshotKnowledgeSourceCommand(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string? Revision)
    : IRequest<Result<SourceSnapshot>>;
