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

    public async Task WriteAsync(StreamGrainState newState)
    {
        state.State = newState;
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

    public async Task UpdateLastSyncAsync(DateTimeOffset lastSync)
    {
        if (state.State.Binding is null) return;
        state.State.Binding.LastSync = lastSync;
        await state.WriteStateAsync();
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != ConnectorPollReminderName) return Task.CompletedTask;
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
                logger.LogError(ex, "Connector poll failed for stream {StreamId}.", streamId);
            }
        });

        return Task.CompletedTask;
    }
}
