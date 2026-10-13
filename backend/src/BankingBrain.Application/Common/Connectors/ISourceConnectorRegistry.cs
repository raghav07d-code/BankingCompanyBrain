namespace BankingBrain.Application.Common.Connectors;

public interface ISourceConnectorRegistry
{
    ISourceConnector Get(string connectorKey);

    bool TryGet(string connectorKey, out ISourceConnector? connector);
}
