using Crucible.Chains.Handlers;
using Crucible.Domain.Results;
using Observa.Features.Streams.Aggregates;
using Stream = Observa.Features.Streams.Aggregates.Stream;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Handlers;

public sealed class IngestEventHandler(IGrainFactory grains)
    : IStepHandler<Stream, StreamId, IngestEventDto, FlowEventIngested>
{
    public async Task<Result> InvokeAsync(
        Stream agg,
        IngestEventDto input,
        FlowEventIngested stepResult,
        CancellationToken ct)
    {
        var grain = grains.GetGrain<IStreamGrain>(agg.Id.Value);
        await grain.WriteAsync(StreamGrainState.From(agg));
        return Result.Success();
    }
}
