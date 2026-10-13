using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Semantics.Commands.ExtractSemantics;

public sealed record ExtractSemanticsCommand(
    Guid WorkspaceId,
    Guid DocumentId,
    Guid VersionId)
    : ICommand<ExtractSemanticsResponse>;
