using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocuments;

public sealed class GetDocumentsHandler
    : IQueryHandler<GetDocumentsQuery, IReadOnlyList<GetDocumentsResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IDocumentRepository _documentRepository;

    public GetDocumentsHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IDocumentRepository documentRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _documentRepository = documentRepository;
    }

    public async Task<Result<IReadOnlyList<GetDocumentsResponse>>> Handle(
        GetDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<GetDocumentsResponse>>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<IReadOnlyList<GetDocumentsResponse>>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var documents = await _documentRepository.GetByWorkspaceAsync(
            workspace.Id,
            cancellationToken);

        return Result<IReadOnlyList<GetDocumentsResponse>>.Success(
            documents.Select(document => new GetDocumentsResponse(
                document.Id,
                document.WorkspaceId,
                document.KnowledgeSourceId,
                document.Name,
                document.ExternalIdentifier,
                document.MimeType,
                document.IsActive)).ToList());
    }
}