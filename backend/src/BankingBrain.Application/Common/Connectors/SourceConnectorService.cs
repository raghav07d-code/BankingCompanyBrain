namespace BankingBrain.Application.Common.Connectors;

public sealed class SourceConnectorService : ISourceConnectorService
{
    private readonly ISourceConnectorRegistry _registry;

    public SourceConnectorService(
        ISourceConnectorRegistry registry)
    {
        _registry = registry;
    }

    public Task<SourceSnapshot> SnapshotAsync(
        SourceConnectorRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectorKey))
            throw new ArgumentException(
                "Connector key is required.",
                nameof(request));

        if (string.IsNullOrWhiteSpace(request.ExternalIdentifier))
            throw new ArgumentException(
                "External identifier is required.",
                nameof(request));

        var connector = _registry.Get(
            request.ConnectorKey);

        return connector.SnapshotAsync(
            request,
            cancellationToken);
    }
}