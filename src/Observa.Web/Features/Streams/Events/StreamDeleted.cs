using Crucible.Domain.Events;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Events;

public sealed record StreamDeleted(StreamId StreamId) : DomainEvent;
