using BankingBrain.Application.Common.Connectors;

namespace BankingBrain.Infrastructure.Connectors;

public sealed class SourceConnectorRegistry : ISourceConnectorRegistry
{
    private readonly IReadOnlyDictionary<string, ISourceConnector> _connectors;

    public SourceConnectorRegistry(IEnumerable<ISourceConnector> connectors)
    {
        var map = new Dictionary<string, ISourceConnector>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var connector in connectors)
        {
            if (string.IsNullOrWhiteSpace(connector.ConnectorKey))
                throw new InvalidOperationException(
                    "A source connector must define a connector key.");

            if (!map.TryAdd(connector.ConnectorKey, connector))
            {
                throw new InvalidOperationException(
                    $"Duplicate source connector key '{connector.ConnectorKey}'.");
            }
        }

        _connectors = map;
    }

    public ISourceConnector Get(string connectorKey)
    {
        if (string.IsNullOrWhiteSpace(connectorKey))
            throw new ArgumentException(
                "Connector key is required.",
                nameof(connectorKey));

        if (!_connectors.TryGetValue(connectorKey, out var connector))
        {
            throw new KeyNotFoundException(
                $"Source connector '{connectorKey}' is not registered.");
        }

        return connector;
    }

    public bool TryGet(
        string connectorKey,
        out ISourceConnector? connector)
    {
        if (string.IsNullOrWhiteSpace(connectorKey))
        {
            connector = null;
            return false;
        }

        return _connectors.TryGetValue(
            connectorKey,
            out connector);
    }
}
