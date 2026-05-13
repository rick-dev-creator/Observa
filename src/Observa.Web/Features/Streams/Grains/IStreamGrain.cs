using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Grains;

public interface IStreamGrain : IGrainWithGuidKey
{
    Task<StreamGrainState> GetAsync();
    Task WriteAsync(StreamGrainState newState);
    Task EnsureScheduleReminderAsync(Recurrence schedule);
    Task RemoveScheduleReminderAsync();
}
