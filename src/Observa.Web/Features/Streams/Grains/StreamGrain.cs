using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Orchestration;
using Observa.Features.Connectors.Registry;
using Observa.Features.Streams.Enums;
using Orleans.Runtime;

namespace Observa.Features.Streams.Grains;

public sealed class StreamGrain(
    [PersistentState("stream")] IPersistentState<StreamGrainState> state,
    ILogger<StreamGrain> logger)
    : Grain, IStreamGrain, IRemindable
{
    private const string ConnectorPollReminderName = "connector-poll";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Re-register the poll reminder from the connector's *current* configured interval.
        // Orleans persists the reminder period, so a config change (e.g. fixing a poll interval)
        // would otherwise never take effect for already-registered streams. Re-ensuring here means
        // the interval is corrected the next time the grain activates (dashboard view, restart, …).
        if (state.State.Status == StreamStatus.Active && state.State.Binding is { } binding)
        {
            var connector = ServiceProvider.GetRequiredService<IConnectorRegistry>()
                .Find(new ConnectorId(binding.ConnectorId));
            if (connector is { Metadata.PollInterval: var pi } && pi > TimeSpan.Zero)
            {
                await EnsureConnectorPollReminderAsync(pi);

                // Catch-up poll: if a full interval has elapsed since the last poll (a restart, downtime,
                // or a just-corrected poll interval), poll now instead of waiting a whole interval for the
                // reminder's first tick. A healthy stream polls every interval, so its LastConnectorPollAt
                // stays fresh and routine reactivations do NOT trigger an extra poll.
                var last = state.State.LastConnectorPollAt;
                if (last is null || DateTimeOffset.UtcNow - last.Value >= pi)
                    KickOffPoll();
            }
        }
    }

    private void KickOffPoll()
    {
        var streamId = this.GetPrimaryKey();
        _ = Task.Run(async () =>
        {
            try
            {
                var orchestrator = ServiceProvider.GetRequiredService<ConnectorPollOrchestrator>();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                await orchestrator.PollAsync(streamId, cts.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Catch-up connector poll failed for stream {StreamId}.", streamId);
            }
        });
    }

    public Task<StreamGrainState> GetAsync() => Task.FromResult(state.State);

    public async Task WriteAsync(StreamGrainState newState, ActivityLogEntry? logEntry = null)
    {
        newState.ActivityLog = state.State.ActivityLog;
        newState.LastConnectorPollAt = state.State.LastConnectorPollAt;
        // SnapshotState is grain-owned (set by SetConnectorSnapshotStateAsync); preserve it if the
        // incoming state didn't carry it but the existing binding had one.
        if (newState.Binding is not null && newState.Binding.SnapshotState is null && state.State.Binding?.SnapshotState is not null)
            newState.Binding.SnapshotState = state.State.Binding.SnapshotState;
        if (newState.Binding is not null && newState.Binding.CapitalBasisUsd is null && state.State.Binding?.CapitalBasisUsd is not null)
            newState.Binding.CapitalBasisUsd = state.State.Binding.CapitalBasisUsd;
        state.State = newState;

        if (logEntry is not null)
            state.State.AppendActivityLog(logEntry);

        await state.WriteStateAsync();
    }

    public async Task LogActivityAsync(ActivityLogEntry entry)
    {
        state.State.AppendActivityLog(entry);
        await state.WriteStateAsync();
    }

    public async Task MarkPolledAsync(DateTimeOffset at)
    {
        state.State.LastConnectorPollAt = at;
        await state.WriteStateAsync();
    }

    public async Task SetConnectorSnapshotStateAsync(string? snapshotState, decimal? capitalBasisUsd)
    {
        if (state.State.Binding is null) return;
        state.State.Binding.SnapshotState = snapshotState;
        state.State.Binding.CapitalBasisUsd = capitalBasisUsd;
        await state.WriteStateAsync();
    }

    public async Task EnsureConnectorPollReminderAsync(TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero) return;
        await this.RegisterOrUpdateReminder(ConnectorPollReminderName, pollInterval, pollInterval);
    }

    public async Task RemoveConnectorPollReminderAsync()
    {
        var existing = await this.GetReminder(ConnectorPollReminderName);
        if (existing is not null)
            await this.UnregisterReminder(existing);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != ConnectorPollReminderName) return;
        var now = DateTimeOffset.UtcNow;

        // LastConnectorPollAt is owned by the poll path (orchestrator → MarkPolledAsync),
        // so it stays accurate for both reminder fires and the initial poll on registration.
        state.State.AppendActivityLog(new ActivityLogEntry
        {
            Timestamp = now,
            Kind = "ReminderFired",
            Message = $"Connector poll reminder fired (TickStatus={status})",
        });
        await state.WriteStateAsync();

        KickOffPoll();
    }
}
