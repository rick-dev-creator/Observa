using Crucible.Domain.Events;

namespace Observa.Features.Streams.Events;

public sealed record StreamRegistered(
    StreamId StreamId,
    string Name,
    Direction Direction,
    string Category) : DomainEvent;
