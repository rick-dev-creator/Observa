namespace Observa.Connectors.Abstractions;

/// <summary>
/// A connector whose natural datum is a current USD value (not events). The orchestrator samples it,
/// persists its opaque <see cref="SnapshotSample.State"/> on the binding, and ingests the capital-netted
/// <see cref="SnapshotSample.PerformanceDeltaUsd"/> as a signed Performance event. Inherits IConnector so it
/// appears in the registry/catalog; its FetchEventsAsync returns an empty list (unused).
/// </summary>
public interface ISnapshotConnector : IConnector
{
    Task<SnapshotSample> SampleAsync(SnapshotContext context, CancellationToken ct);
}
