using Crucible.Chains.Handlers;
using Crucible.Chains.Steps;
using Crucible.Domain.Results;
using Observa.Features.Streams.Aggregates;
using Stream = Observa.Features.Streams.Aggregates.Stream;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Handlers;

public sealed class ResumeStreamHandler(IGrainFactory grains)
    : IStepHandler<Stream, StreamId, Unit, StreamResumed>
{
    public async Task<Result> InvokeAsync(
        Stream agg,
        Unit input,
        StreamResumed stepResult,
        CancellationToken ct)
    {
        var grain = grains.GetGrain<IStreamGrain>(agg.Id.Value);
        await grain.WriteAsync(StreamGrainState.From(agg));

        if (agg.Schedule is { } schedule)
            await grain.EnsureScheduleReminderAsync(schedule);

        return Result.Success();
    }
}
