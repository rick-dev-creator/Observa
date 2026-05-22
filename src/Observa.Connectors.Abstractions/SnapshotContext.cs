namespace Observa.Connectors.Abstractions;

public sealed record SnapshotContext(
    Guid CallerId,
    string ExternalRef,
    string? PreviousState);
