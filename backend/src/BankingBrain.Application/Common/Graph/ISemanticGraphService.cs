namespace BankingBrain.Application.Common.Graph;

public interface ISemanticGraphService
{
    Task RebuildAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<SemanticGraphResult> QueryAsync(Guid workspaceId, SemanticGraphQuery query, CancellationToken cancellationToken);
}