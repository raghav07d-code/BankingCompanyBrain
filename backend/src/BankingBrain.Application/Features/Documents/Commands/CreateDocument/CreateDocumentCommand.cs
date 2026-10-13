using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Documents.Commands.CreateDocument;

public sealed record CreateDocumentCommand(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string Name,
    string? ExternalIdentifier,
    string MimeType)
    : ICommand<CreateDocumentResponse>;