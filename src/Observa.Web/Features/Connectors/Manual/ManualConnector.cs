using Observa.Connectors.Abstractions;

namespace Observa.Features.Connectors.Manual;

public sealed class ManualConnector : IConnector
{
    public static readonly ConnectorId ConnectorId = new("manual");

    public ConnectorId Id => ConnectorId;

    public ConnectorMetadata Metadata { get; } = new(
        DisplayName: "Manual",
        Description: "Events are recorded by hand from the dashboard. No external polling.",
        PollInterval: TimeSpan.Zero,
        ConfigSchema: []);

    public Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(
        ConnectorFetchContext context,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConnectorFlowEvent>>([]);
}
