using Crucible.Domain.Identifiers;

namespace Observa.Features.Streams;

public readonly record struct StreamId(Guid Value) : IAggregateId<StreamId>
{
    public static StreamId New() => new(Guid.NewGuid());
    public static StreamId From(Guid value) => new(value);
}
