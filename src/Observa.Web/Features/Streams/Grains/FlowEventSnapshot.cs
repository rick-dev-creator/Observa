using Observa.Features.Streams.Entities;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class FlowEventSnapshot
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public DateTimeOffset OccurredAt { get; set; }
    [Id(2)] public MoneyState Amount { get; set; } = new();
    [Id(3)] public IngestionSource Source { get; set; }
    [Id(4)] public string? ExternalRef { get; set; }

    public static FlowEventSnapshot From(FlowEvent ev) => new()
    {
        Id = ev.Id.Value,
        OccurredAt = ev.OccurredAt,
        Amount = MoneyState.From(ev.Amount),
        Source = ev.Source,
        ExternalRef = ev.ExternalRef,
    };

    public IFlowEventSnapshot AsCrucibleSnapshot() => new View(this);

    private sealed class View(FlowEventSnapshot s) : IFlowEventSnapshot
    {
        public FlowEventId Id => FlowEventId.From(s.Id);
        public DateTimeOffset OccurredAt => s.OccurredAt;
        public Money Amount => s.Amount.ToDomain();
        public IngestionSource Source => s.Source;
        public string? ExternalRef => s.ExternalRef;
    }
}
