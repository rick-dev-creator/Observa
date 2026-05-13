using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Registry;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.Services.Views;

namespace Observa.Features.Streams.Services;

public sealed class StreamQueryService(IGrainFactory grains, IConnectorRegistry connectors)
{
    public async Task<IReadOnlyList<StreamOperationsView>> ListOperationsAsync(
        bool includeTerminal,
        CancellationToken ct)
    {
        var index = grains.GetGrain<IStreamIndexGrain>(StreamIndexGrain.SingletonKey);
        var ids = await index.GetAllAsync();
        var rows = new List<StreamOperationsView>(ids.Count);

        foreach (var id in ids)
        {
            var state = await grains.GetGrain<IStreamGrain>(id).GetAsync();
            if (!includeTerminal && state.Status is StreamStatus.Stopped or StreamStatus.Deleted)
                continue;

            rows.Add(MapOperations(state));
        }

        return rows.OrderByDescending(r => r.LastEventAt ?? DateTimeOffset.MinValue).ToList();
    }

    public async Task<StreamActivityView?> GetActivityAsync(StreamId id, CancellationToken ct)
    {
        var state = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        if (state.Id == Guid.Empty) return null;

        return new StreamActivityView(
            Id: state.Id,
            Name: state.Name,
            Status: state.Status,
            ReminderStatus: MapReminderStatus(state),
            ActivityLog: state.ActivityLog
                .OrderByDescending(e => e.Timestamp)
                .Select(MapLogEntry)
                .ToList());
    }

    public async Task<IReadOnlyList<FlowEventView>> ListEventsAsync(StreamId id, CancellationToken ct)
    {
        var state = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        return state.Events
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new FlowEventView(e.Id, e.OccurredAt, e.Amount.Amount, e.Source, e.ExternalRef))
            .ToList();
    }


    private StreamOperationsView MapOperations(StreamGrainState state)
    {
        IConnector? connector = null;
        if (state.Binding is { } binding)
            connector = connectors.Find(new ConnectorId(binding.ConnectorId));

        var nextFire = state.LastConnectorPollAt is { } last && connector is not null
            ? last + connector.Metadata.PollInterval
            : (DateTimeOffset?)null;

        var lastPollFailed = state.ActivityLog
            .OrderByDescending(e => e.Timestamp)
            .Take(5)
            .Any(e => e.Kind == "PollFailed");

        var lastEvent = state.Events.MaxBy(e => e.OccurredAt);

        return new StreamOperationsView(
            Id: state.Id,
            Name: state.Name,
            Category: state.Category,
            Direction: state.Direction,
            Status: state.Status,
            ConnectorId: state.Binding?.ConnectorId,
            ConnectorDisplayName: connector?.Metadata.DisplayName,
            LastConnectorPollAt: state.LastConnectorPollAt,
            NextPollEstimate: nextFire,
            LastPollFailed: lastPollFailed,
            LastEventAt: lastEvent?.OccurredAt,
            LastEventAmount: lastEvent?.Amount.Amount,
            ExpectedAmount: state.ExpectedAmount?.Amount);
    }

    private ReminderStatusView? MapReminderStatus(StreamGrainState state)
    {
        if (state.Binding is null) return null;
        var connector = connectors.Find(new ConnectorId(state.Binding.ConnectorId));
        if (connector is null) return null;
        if (connector.Metadata.PollInterval <= TimeSpan.Zero) return null;

        var next = state.LastConnectorPollAt is { } last
            ? last + connector.Metadata.PollInterval
            : (DateTimeOffset?)null;

        return new ReminderStatusView(
            ConnectorId: state.Binding.ConnectorId,
            ConnectorDisplayName: connector.Metadata.DisplayName,
            PollInterval: connector.Metadata.PollInterval,
            LastFiredAt: state.LastConnectorPollAt,
            NextFireEstimate: next,
            LastSyncAt: state.Binding.LastSync);
    }

    private static ActivityLogEntryView MapLogEntry(ActivityLogEntry entry) =>
        new(entry.Timestamp, entry.Kind, entry.Message, entry.Details);
}
