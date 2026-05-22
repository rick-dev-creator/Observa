using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Observa.Features.Connectors.Orchestration;
using Orleans.Runtime;

namespace Observa.Features.Streams.Grains;

public sealed class StreamGrain(
    [PersistentState("stream")] IPersistentState<StreamGrainState> state,
    ILogger<StreamGrain> logger)
    : Grain, IStreamGrain, IRemindable
{
    private const string ConnectorPollReminderName = "connector-poll";

    public Task<StreamGrainState> GetAsync() => Task.FromResult(state.State);

    public async Task WriteAsync(StreamGrainState newState, ActivityLogEntry? logEntry = null)
    {
        newState.ActivityLog = state.State.ActivityLog;
        newState.LastConnectorPollAt = state.State.LastConnectorPollAt;
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

    public async Task SetConnectorSnapshotStateAsync(string? snapshotState)
    {
        if (state.State.Binding is null) return;
        state.State.Binding.SnapshotState = snapshotState;
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
        var streamId = this.GetPrimaryKey();
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
                logger.LogError(ex, "Connector poll failed for stream {StreamId}.", streamId);
            }
        });
    }
}
