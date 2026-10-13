using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocumentVersions;

public sealed record GetDocumentVersionsQuery(Guid WorkspaceId, Guid DocumentId)
    : IQuery<IReadOnlyList<DocumentVersionResponse>>;