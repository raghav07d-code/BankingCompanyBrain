using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Domain.Entities.KnowledgeSource;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.CreateKnowledgeSource;

public sealed class CreateKnowledgeSourceHandler
    : ICommandHandler<CreateKnowledgeSourceCommand, CreateKnowledgeSourceResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;

    public CreateKnowledgeSourceHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
    }

    public async Task<Result<CreateKnowledgeSourceResponse>> Handle(
        CreateKnowledgeSourceCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CreateKnowledgeSourceResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<CreateKnowledgeSourceResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var exists = await _sourceRepository.ExistsAsync(
            workspace.Id,
            request.Name.Trim(),
            cancellationToken);

        if (exists)
            return Result<CreateKnowledgeSourceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.DUPLICATE",
                    "A knowledge source with this name already exists."));

        var source = KnowledgeSource.Create(
            workspace.Id,
            request.Name,
            request.Description,
            request.SourceType,
            request.ConnectorKey,
            request.ExternalIdentifier);

        await _sourceRepository.AddAsync(source, cancellationToken);
        var saved = await _sourceRepository.SaveChangesAsync(cancellationToken);

        if (!saved)
            return Result<CreateKnowledgeSourceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.DUPLICATE",
                    "A knowledge source with this name already exists."));

        return Result<CreateKnowledgeSourceResponse>.Success(
            new CreateKnowledgeSourceResponse(
                source.Id,
                source.WorkspaceId,
                source.Name,
                source.Description,
                source.SourceType,
                source.IsActive));
    }
}