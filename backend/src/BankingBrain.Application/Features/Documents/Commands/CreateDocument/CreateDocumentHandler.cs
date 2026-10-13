using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Domain.Entities.Document;

namespace BankingBrain.Application.Features.Documents.Commands.CreateDocument;

public sealed class CreateDocumentHandler
    : ICommandHandler<CreateDocumentCommand, CreateDocumentResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;
    private readonly IDocumentRepository _documentRepository;

    public CreateDocumentHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository,
        IDocumentRepository documentRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
        _documentRepository = documentRepository;
    }

    public async Task<Result<CreateDocumentResponse>> Handle(
        CreateDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CreateDocumentResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<CreateDocumentResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var source = await _sourceRepository.GetByIdAsync(
            workspace.Id,
            request.KnowledgeSourceId,
            cancellationToken);

        if (source is null)
            return Result<CreateDocumentResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_FOUND",
                    "Knowledge source was not found."));

        var document = Document.Create(
            workspace.Id,
            source.Id,
            request.Name,
            request.ExternalIdentifier,
            request.MimeType);

        await _documentRepository.AddAsync(document, cancellationToken);
        var saved = await _documentRepository.SaveChangesAsync(cancellationToken);

        if (!saved)
            return Result<CreateDocumentResponse>.Failure(
                new Error(
                    "DOCUMENT.DUPLICATE",
                    "A document with this name already exists for the source."));

        return Result<CreateDocumentResponse>.Success(
            new CreateDocumentResponse(
                document.Id,
                document.WorkspaceId,
                document.KnowledgeSourceId,
                document.Name,
                document.ExternalIdentifier,
                document.MimeType,
                document.IsActive));
    }
}