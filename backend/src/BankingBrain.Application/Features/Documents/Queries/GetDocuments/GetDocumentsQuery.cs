using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocuments;

public sealed record GetDocumentsQuery(Guid WorkspaceId)
    : IQuery<IReadOnlyList<GetDocumentsResponse>>;