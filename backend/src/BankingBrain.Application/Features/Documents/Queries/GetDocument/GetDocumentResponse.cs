namespace BankingBrain.Application.Features.Documents.Queries.GetDocument;

public sealed record GetDocumentResponse(
    Guid DocumentId,
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string Name,
    string? ExternalIdentifier,
    string MimeType,
    bool IsActive);