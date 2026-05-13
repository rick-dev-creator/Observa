using Crucible.Domain.Events;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Events;

public sealed record StreamRegistered(
    StreamId StreamId,
    string Name,
    Direction Direction,
    string Category) : DomainEvent;
