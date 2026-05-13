using Crucible.Chains.Handlers;
using Crucible.Chains.Steps;
using Crucible.Domain.Results;
using Observa.Features.Streams.Aggregates;
using Stream = Observa.Features.Streams.Aggregates.Stream;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Handlers;

public sealed class PauseStreamHandler(IGrainFactory grains)
    : IStepHandler<Stream, StreamId, Unit, StreamPaused>
{
    public async Task<Result> InvokeAsync(
        Stream agg,
        Unit input,
        StreamPaused stepResult,
        CancellationToken ct)
    {
        var grain = grains.GetGrain<IStreamGrain>(agg.Id.Value);
        await grain.WriteAsync(StreamGrainState.From(agg), new ActivityLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = "Paused",
            Message = "Stream paused",
        });
        await grain.RemoveConnectorPollReminderAsync();
        return Result.Success();
    }
}
