using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Documents.Commands.IngestDocument;

public sealed record IngestDocumentCommand(
    Guid WorkspaceId,
    Guid DocumentId,
    string FileName,
    string MediaType,
    Stream Content,
    long FileSize)
    : ICommand<IngestDocumentResponse>;