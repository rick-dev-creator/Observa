namespace Observa.Features.Streams.Grains;

public interface IStreamGrain : IGrainWithGuidKey
{
    Task<StreamGrainState> GetAsync();
    Task WriteAsync(StreamGrainState newState);
    Task EnsureConnectorPollReminderAsync(TimeSpan pollInterval);
    Task RemoveConnectorPollReminderAsync();
    Task UpdateLastSyncAsync(DateTimeOffset lastSync);
}
