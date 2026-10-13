namespace BankingBrain.Application.Common.Connectors;

public interface ISourceConnector
{
    string ConnectorKey { get; }

    Task<SourceSnapshot> SnapshotAsync(
        SourceConnectorRequest request,
        CancellationToken cancellationToken);
}