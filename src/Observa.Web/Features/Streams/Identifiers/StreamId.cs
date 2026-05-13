using Crucible.Domain.Identifiers;

namespace Observa.Features.Streams.Identifiers;

[GenerateSerializer]
public readonly record struct StreamId([property: Id(0)] Guid Value) : IAggregateId<StreamId>
{
    public static StreamId New() => new(Guid.NewGuid());
    public static StreamId From(Guid value) => new(value);
}
