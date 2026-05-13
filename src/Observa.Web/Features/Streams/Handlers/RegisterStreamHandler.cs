using Crucible.Chains.Handlers;
using Crucible.Domain.Results;
using Observa.Features.Connectors.Registry;
using Observa.Features.Streams.Aggregates;
using Stream = Observa.Features.Streams.Aggregates.Stream;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Handlers;

public sealed class RegisterStreamHandler(IGrainFactory grains, IConnectorRegistry connectors)
    : IStepHandler<Stream, StreamId, RegisterStreamDto, StreamRegistered>
{
    public async Task<Result> InvokeAsync(
        Stream agg,
        RegisterStreamDto input,
        StreamRegistered stepResult,
        CancellationToken ct)
    {
        var grain = grains.GetGrain<IStreamGrain>(agg.Id.Value);

        var details = new Dictionary<string, string>
        {
            ["Direction"] = agg.Direction.ToString(),
            ["Category"] = agg.Category,
            ["Connector"] = agg.Binding?.ConnectorId.Value ?? "none",
        };
        if (agg.Schedule is { } sched)
            details["Schedule"] = $"{sched.Cadence}/{sched.Anchor}";
        if (agg.ExpectedAmount is { } exp)
            details["ExpectedAmount"] = exp.Amount.ToString("F2");

        await grain.WriteAsync(StreamGrainState.From(agg), new ActivityLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = "Registered",
            Message = $"Stream '{agg.Name}' registered",
            Details = details,
        });

        if (agg.Binding is { } binding
            && connectors.Find(binding.ConnectorId) is { Metadata.PollInterval: var pi }
            && pi > TimeSpan.Zero)
        {
            await grain.EnsureConnectorPollReminderAsync(pi);
        }

        var index = grains.GetGrain<IStreamIndexGrain>(StreamIndexGrain.SingletonKey);
        await index.AddAsync(agg.Id.Value);

        return Result.Success();
    }
}
