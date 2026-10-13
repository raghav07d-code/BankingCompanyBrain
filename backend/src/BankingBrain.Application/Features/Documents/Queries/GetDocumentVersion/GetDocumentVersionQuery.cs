using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocumentVersion;

public sealed record GetDocumentVersionQuery(
    Guid WorkspaceId,
    Guid DocumentId,
    Guid VersionId)
    : IQuery<GetDocumentVersionResponse>;