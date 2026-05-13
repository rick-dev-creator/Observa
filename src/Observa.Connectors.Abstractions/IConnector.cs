namespace Observa.Connectors.Abstractions;

public interface IConnector
{
    ConnectorId Id { get; }
    ConnectorMetadata Metadata { get; }

    Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(
        ConnectorFetchContext context,
        CancellationToken ct);
}
