using Observa.Features.Streams.Aggregates;
using Observa.Features.Streams.Entities;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class StreamGrainState
{
    [Id(0)] public StreamId Id { get; set; }
    [Id(1)] public long Version { get; set; }
    [Id(2)] public string Name { get; set; } = "";
    [Id(3)] public string Category { get; set; } = "";
    [Id(4)] public Direction Direction { get; set; }
    [Id(5)] public Recurrence? Schedule { get; set; }
    [Id(6)] public Money? ExpectedAmount { get; set; }
    [Id(7)] public StreamStatus Status { get; set; }
    [Id(8)] public List<FlowEventSnapshot> Events { get; set; } = new();

    public static StreamGrainState From(Aggregates.Stream agg) => new()
    {
        Id = agg.Id,
        Version = agg.Version,
        Name = agg.Name,
        Category = agg.Category,
        Direction = agg.Direction,
        Schedule = agg.Schedule,
        ExpectedAmount = agg.ExpectedAmount,
        Status = agg.Status,
        Events = agg.Events.Select(FlowEventSnapshot.From).ToList(),
    };

    public IStreamSnapshot AsCrucibleSnapshot() => new View(this);

    private sealed class View(StreamGrainState s) : IStreamSnapshot
    {
        public StreamId Id => s.Id;
        public long Version => s.Version;
        public string Name => s.Name;
        public string Category => s.Category;
        public Direction Direction => s.Direction;
        public Recurrence? Schedule => s.Schedule;
        public Money? ExpectedAmount => s.ExpectedAmount;
        public StreamStatus Status => s.Status;
        public IReadOnlyList<IFlowEventSnapshot> Events { get; } =
            s.Events.Select(e => e.AsCrucibleSnapshot()).ToList();
    }
}
