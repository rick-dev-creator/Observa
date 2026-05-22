namespace Observa.Connectors.Abstractions;

public sealed record SnapshotSample(
    string State,
    decimal PerformanceDeltaUsd,   // now: the CHANGE IN MARKET VALUE since last poll (first poll = full value)
    bool HasPrevious,
    decimal CapitalBasisUsd);      // cumulative net capital invested (for return = value − capital)
