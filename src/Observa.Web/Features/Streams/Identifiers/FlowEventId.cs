namespace Observa.Features.Streams.Identifiers;

public readonly record struct FlowEventId(Guid Value)
{
    public static FlowEventId New() => new(Guid.NewGuid());
    public static FlowEventId From(Guid value) => new(value);
}
