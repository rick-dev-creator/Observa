using Crucible.Domain.Events;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Events;

public sealed record FlowEventIngested(
    StreamId StreamId,
    FlowEventId FlowEventId,
    Money Amount,
    DateTimeOffset FlowOccurredAt) : DomainEvent;
