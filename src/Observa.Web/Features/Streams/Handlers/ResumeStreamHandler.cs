using Crucible.Chains.Handlers;
using Crucible.Chains.Steps;
using Crucible.Domain.Results;
using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Registry;
using Observa.Features.Streams.Aggregates;
using Stream = Observa.Features.Streams.Aggregates.Stream;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Handlers;

public sealed class ResumeStreamHandler(IGrainFactory grains, IConnectorRegistry connectors)
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

        if (agg.Binding is { } binding
            && connectors.Find(binding.ConnectorId) is { Metadata.PollInterval: var pi }
            && pi > TimeSpan.Zero)
        {
            await grain.EnsureConnectorPollReminderAsync(pi);
        }

        return Result.Success();
    }
}
