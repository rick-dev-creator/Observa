namespace Observa.Connectors.Abstractions;

public sealed record SnapshotSample(
    string State,
    decimal PerformanceDeltaUsd,
    bool HasPrevious);
