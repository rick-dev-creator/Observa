using Crucible.Domain.Events;

namespace Observa.Features.Streams.Events;

public sealed record FlowEventIngested(
    StreamId StreamId,
    FlowEventId FlowEventId,
    Money Amount,
    DateTimeOffset FlowOccurredAt) : DomainEvent;
