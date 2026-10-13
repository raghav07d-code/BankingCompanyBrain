using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Documents.Commands.ReprocessDocumentVersion;

public sealed record ReprocessDocumentVersionCommand(
    Guid WorkspaceId,
    Guid DocumentId,
    Guid VersionId)
    : ICommand<ReprocessDocumentVersionResponse>;