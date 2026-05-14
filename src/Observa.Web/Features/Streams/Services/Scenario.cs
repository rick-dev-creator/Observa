using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services;

public sealed record Scenario(
    string Id,
    string Label,
    string Narrative,
    IReadOnlyCollection<Guid> ExcludedStreamIds,
    IReadOnlyDictionary<Guid, decimal> StreamMultipliers,
    IReadOnlyDictionary<string, decimal> CategoryMultipliers,
    IReadOnlyDictionary<Direction, decimal> DirectionMultipliers)
{
    public static Scenario None { get; } = new(
        Id: "none",
        Label: "Sin cambios",
        Narrative: "Tus números actuales.",
        ExcludedStreamIds: Array.Empty<Guid>(),
        StreamMultipliers: new Dictionary<Guid, decimal>(),
        CategoryMultipliers: new Dictionary<string, decimal>(),
        DirectionMultipliers: new Dictionary<Direction, decimal>());

    public static Scenario ExcludeStream(string id, string label, string narrative, Guid streamId) => new(
        id, label, narrative,
        ExcludedStreamIds: new[] { streamId },
        StreamMultipliers: new Dictionary<Guid, decimal>(),
        CategoryMultipliers: new Dictionary<string, decimal>(),
        DirectionMultipliers: new Dictionary<Direction, decimal>());

    public static Scenario MultiplyStream(string id, string label, string narrative, Guid streamId, decimal factor) => new(
        id, label, narrative,
        ExcludedStreamIds: Array.Empty<Guid>(),
        StreamMultipliers: new Dictionary<Guid, decimal> { [streamId] = factor },
        CategoryMultipliers: new Dictionary<string, decimal>(),
        DirectionMultipliers: new Dictionary<Direction, decimal>());

    public static Scenario MultiplyCategory(string id, string label, string narrative, string category, decimal factor) => new(
        id, label, narrative,
        ExcludedStreamIds: Array.Empty<Guid>(),
        StreamMultipliers: new Dictionary<Guid, decimal>(),
        CategoryMultipliers: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [category] = factor },
        DirectionMultipliers: new Dictionary<Direction, decimal>());

    public static Scenario MultiplyDirection(string id, string label, string narrative, Direction direction, decimal factor) => new(
        id, label, narrative,
        ExcludedStreamIds: Array.Empty<Guid>(),
        StreamMultipliers: new Dictionary<Guid, decimal>(),
        CategoryMultipliers: new Dictionary<string, decimal>(),
        DirectionMultipliers: new Dictionary<Direction, decimal> { [direction] = factor });
}
