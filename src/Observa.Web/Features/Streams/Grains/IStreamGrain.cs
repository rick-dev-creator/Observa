namespace Observa.Features.Streams.Grains;

public interface IStreamGrain : IGrainWithGuidKey
{
    Task<StreamGrainState> GetAsync();
    Task WriteAsync(StreamGrainState newState, ActivityLogEntry? logEntry = null);
    Task LogActivityAsync(ActivityLogEntry entry);
    Task MarkPolledAsync(DateTimeOffset at);
    Task SetConnectorSnapshotStateAsync(string? snapshotState, decimal? capitalBasisUsd);
    Task EnsureConnectorPollReminderAsync(TimeSpan pollInterval);
    Task RemoveConnectorPollReminderAsync();
}
