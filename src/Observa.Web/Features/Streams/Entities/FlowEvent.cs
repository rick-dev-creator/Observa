using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Entities;

[Entity]
public partial class FlowEvent : Entity<FlowEventId>
{
    public DateTimeOffset OccurredAt { get; private set; }
    public Money Amount { get; private set; } = Money.Zero;
    public IngestionSource Source { get; private set; }
    public string? ExternalRef { get; private set; }

    private FlowEvent() { }

    internal FlowEvent(FlowEventId id, DateTimeOffset occurredAt, Money amount, IngestionSource source, string? externalRef)
    {
        Id = id;
        OccurredAt = occurredAt;
        Amount = amount;
        Source = source;
        ExternalRef = externalRef;
    }
}
