using Crucible.Chains.Handlers;
using Crucible.Domain.Results;
using Observa.Features.Streams.Aggregates;
using Stream = Observa.Features.Streams.Aggregates.Stream;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Handlers;

public sealed class RecordPollHandler(IGrainFactory grains)
    : IStepHandler<Stream, StreamId, DateTimeOffset, ConnectorPolled>
{
    public async Task<Result> InvokeAsync(
        Stream agg,
        DateTimeOffset input,
        ConnectorPolled stepResult,
        CancellationToken ct)
    {
        var grain = grains.GetGrain<IStreamGrain>(agg.Id.Value);
        await grain.WriteAsync(StreamGrainState.From(agg), new ActivityLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = "PollRecorded",
            Message = $"Connector poll recorded at {stepResult.PolledAt:O}",
            Details = new Dictionary<string, string>
            {
                ["PolledAt"] = stepResult.PolledAt.ToString("O"),
                ["ConnectorId"] = agg.Binding?.ConnectorId.Value ?? "",
            },
        });
        return Result.Success();
    }
}
