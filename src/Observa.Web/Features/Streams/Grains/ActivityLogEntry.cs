namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class ActivityLogEntry
{
    [Id(0)] public DateTimeOffset Timestamp { get; set; }
    [Id(1)] public string Kind { get; set; } = "";
    [Id(2)] public string Message { get; set; } = "";
    [Id(3)] public Dictionary<string, string>? Details { get; set; }
}
