using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Common.Connectors;

public sealed class KnowledgeSourceSnapshotService
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _knowledgeSourceRepository;
    private readonly ISourceConnectorRegistry _connectorRegistry;

    public KnowledgeSourceSnapshotService(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository knowledgeSourceRepository,
        ISourceConnectorRegistry connectorRegistry)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _knowledgeSourceRepository = knowledgeSourceRepository;
        _connectorRegistry = connectorRegistry;
    }

    public async Task<SourceSnapshot> SnapshotAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string? revision,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Authentication is required.");

        if (workspaceId == Guid.Empty)
            throw new ArgumentException(
                "Workspace ID is required.",
                nameof(workspaceId));

        if (knowledgeSourceId == Guid.Empty)
            throw new ArgumentException(
                "Knowledge source ID is required.",
                nameof(knowledgeSourceId));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            workspaceId,
            cancellationToken);

        if (workspace is null)
            throw new KeyNotFoundException(
                $"Workspace '{workspaceId}' was not found.");

        var source = await _knowledgeSourceRepository.GetByIdAsync(
            workspaceId,
            knowledgeSourceId,
            cancellationToken);

        if (source is null)
            throw new KeyNotFoundException(
                $"Knowledge source '{knowledgeSourceId}' was not found.");

        if (string.IsNullOrWhiteSpace(source.ConnectorKey))
            throw new InvalidOperationException(
                $"Knowledge source '{source.Id}' does not have a connector key.");

        if (string.IsNullOrWhiteSpace(source.ExternalIdentifier))
            throw new InvalidOperationException(
                $"Knowledge source '{source.Id}' does not have an external identifier.");

        var connector = _connectorRegistry.Get(source.ConnectorKey);

        var request = new SourceConnectorRequest(
            workspaceId,
            source.Id,
            source.ConnectorKey,
            source.ExternalIdentifier,
            revision);

        var snapshot = await connector.SnapshotAsync(
            request,
            cancellationToken);

        if (!string.Equals(
                snapshot.ConnectorKey,
                source.ConnectorKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Connector '{source.ConnectorKey}' returned snapshot identity '{snapshot.ConnectorKey}'.");
        }

        return snapshot;
    }
}