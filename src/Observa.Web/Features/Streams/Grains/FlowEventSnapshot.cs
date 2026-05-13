using Observa.Features.Streams.Entities;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class FlowEventSnapshot
{
    [Id(0)] public FlowEventId Id { get; set; }
    [Id(1)] public DateTimeOffset OccurredAt { get; set; }
    [Id(2)] public Money Amount { get; set; } = Money.Zero;
    [Id(3)] public IngestionSource Source { get; set; }

    public static FlowEventSnapshot From(FlowEvent ev) => new()
    {
        Id = ev.Id,
        OccurredAt = ev.OccurredAt,
        Amount = ev.Amount,
        Source = ev.Source,
    };

    public IFlowEventSnapshot AsCrucibleSnapshot() => new View(this);

    private sealed class View(FlowEventSnapshot s) : IFlowEventSnapshot
    {
        public FlowEventId Id => s.Id;
        public DateTimeOffset OccurredAt => s.OccurredAt;
        public Money Amount => s.Amount;
        public IngestionSource Source => s.Source;
    }
}
