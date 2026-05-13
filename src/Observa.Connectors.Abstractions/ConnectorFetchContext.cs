namespace Observa.Connectors.Abstractions;

public sealed record ConnectorFetchContext(
    Guid CallerId,
    string ExternalRef,
    DateTimeOffset Since);
