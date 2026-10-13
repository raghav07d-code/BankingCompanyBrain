namespace BankingBrain.Application.Common.SqlLineage;

/// <summary>Mirrors <c>ICodeIntelligenceRefreshQueue</c>'s shape exactly, for the same reason: SQL parsing is comparatively cheap but still happens off the request path, after a successful snapshot reconciliation.</summary>
public interface ISqlLineageRefreshQueue
{
    ValueTask QueueRefreshAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken);
}
