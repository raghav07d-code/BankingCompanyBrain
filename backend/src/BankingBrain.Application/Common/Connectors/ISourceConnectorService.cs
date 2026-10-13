namespace BankingBrain.Application.Common.Connectors;

public interface ISourceConnectorService
{
    Task<SourceSnapshot> SnapshotAsync(
        SourceConnectorRequest request,
        CancellationToken cancellationToken);
}
