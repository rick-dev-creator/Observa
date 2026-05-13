namespace Observa.Connectors.Abstractions;

public sealed record ConnectorFlowEvent(
    string ExternalEventId,
    DateTimeOffset OccurredAt,
    decimal AmountUsd,
    string? RawPayload = null);
