namespace Observa.Features.Streams.Identifiers;

[GenerateSerializer]
public readonly record struct FlowEventId([property: Id(0)] Guid Value)
{
    public static FlowEventId New() => new(Guid.NewGuid());
    public static FlowEventId From(Guid value) => new(value);
}
