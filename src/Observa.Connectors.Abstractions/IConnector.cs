namespace Observa.Connectors.Abstractions;

public interface IConnector
{
    ConnectorId Id { get; }
    ConnectorMetadata Metadata { get; }

    Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(
        string externalRef,
        DateTimeOffset since,
        CancellationToken ct);
}
