using Crucible.Domain.Events;
using Observa.Features.Streams.Identifiers;

namespace Observa.Features.Streams.Events;

public sealed record ConnectorPolled(StreamId StreamId, DateTimeOffset PolledAt) : DomainEvent;
