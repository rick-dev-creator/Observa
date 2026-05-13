using Microsoft.Extensions.Logging;
using Observa.Features.Streams.Enums;
using Orleans.Runtime;

namespace Observa.Features.Streams.Grains;

public sealed class StreamGrain(
    [PersistentState("stream")] IPersistentState<StreamGrainState> state,
    ILogger<StreamGrain> logger)
    : Grain, IStreamGrain, IRemindable
{
    private const string ScheduleReminderName = "schedule-poll";

    public Task<StreamGrainState> GetAsync() => Task.FromResult(state.State);

    public async Task WriteAsync(StreamGrainState newState)
    {
        state.State = newState;
        await state.WriteStateAsync();
    }

    public async Task EnsureScheduleReminderAsync(RecurrenceState schedule)
    {
        var period = schedule.Cadence switch
        {
            Cadence.Monthly => TimeSpan.FromDays(30),
            Cadence.Weekly => TimeSpan.FromDays(7),
            Cadence.Biweekly => TimeSpan.FromDays(14),
            _ => TimeSpan.Zero,
        };

        if (period == TimeSpan.Zero) return;

        await this.RegisterOrUpdateReminder(ScheduleReminderName, period, period);
    }

    public async Task RemoveScheduleReminderAsync()
    {
        var existing = await this.GetReminder(ScheduleReminderName);
        if (existing is not null)
            await this.UnregisterReminder(existing);
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        logger.LogInformation(
            "Stream {StreamId} reminder '{Reminder}' fired (Status={Status}); polling not implemented yet",
            state.State.Id, reminderName, status);
        return Task.CompletedTask;
    }
}
