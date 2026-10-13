namespace BankingBrain.Application.Features.Documents.Commands.CreateDocument;

public sealed record CreateDocumentResponse(
    Guid DocumentId,
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string Name,
    string? ExternalIdentifier,
    string MimeType,
    bool IsActive);