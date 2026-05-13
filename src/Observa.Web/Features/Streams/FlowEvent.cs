using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;

namespace Observa.Features.Streams;

[Entity]
public partial class FlowEvent : Entity<FlowEventId>
{
    public DateTimeOffset OccurredAt { get; private set; }
    public Money Amount { get; private set; } = Money.Zero;
    public IngestionSource Source { get; private set; }

    private FlowEvent() { }

    internal FlowEvent(FlowEventId id, DateTimeOffset occurredAt, Money amount, IngestionSource source)
    {
        Id = id;
        OccurredAt = occurredAt;
        Amount = amount;
        Source = source;
    }
}
