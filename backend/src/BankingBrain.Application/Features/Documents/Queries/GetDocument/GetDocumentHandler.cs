using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocument;

public sealed class GetDocumentHandler
    : IQueryHandler<GetDocumentQuery, GetDocumentResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IDocumentRepository _documentRepository;

    public GetDocumentHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IDocumentRepository documentRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _documentRepository = documentRepository;
    }

    public async Task<Result<GetDocumentResponse>> Handle(
        GetDocumentQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<GetDocumentResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<GetDocumentResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var document = await _documentRepository.GetByIdAsync(
            workspace.Id,
            request.DocumentId,
            cancellationToken);

        if (document is null)
            return Result<GetDocumentResponse>.Failure(
                new Error("DOCUMENT.NOT_FOUND", "Document was not found."));

        return Result<GetDocumentResponse>.Success(
            new GetDocumentResponse(
                document.Id,
                document.WorkspaceId,
                document.KnowledgeSourceId,
                document.Name,
                document.ExternalIdentifier,
                document.MimeType,
                document.IsActive));
    }
}