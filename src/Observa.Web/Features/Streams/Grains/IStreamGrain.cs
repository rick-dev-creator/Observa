namespace Observa.Features.Streams.Grains;

public interface IStreamGrain : IGrainWithGuidKey
{
    Task<StreamGrainState> GetAsync();
    Task WriteAsync(StreamGrainState newState);
    Task EnsureScheduleReminderAsync(RecurrenceState schedule);
    Task RemoveScheduleReminderAsync();
}
