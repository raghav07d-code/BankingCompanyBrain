namespace BankingBrain.Application.Features.Documents.Queries.GetDocuments;

public sealed record GetDocumentsResponse(
    Guid DocumentId,
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string Name,
    string? ExternalIdentifier,
    string MimeType,
    bool IsActive);