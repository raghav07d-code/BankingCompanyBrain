using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocument;

public sealed record GetDocumentQuery(
    Guid WorkspaceId,
    Guid DocumentId)
    : IQuery<GetDocumentResponse>;