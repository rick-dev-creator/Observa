using System.Text.Json;

namespace Observa.Connectors.Solana;

/// <summary>Serializes the snapshot state for a Solana token holding: last quantity (q) and last USD price (p).</summary>
internal static class SnapshotStateCodec
{
    public static string Serialize(decimal quantity, decimal price) =>
        JsonSerializer.Serialize(new StatePayload { Q = quantity, P = price });

    public static (decimal Quantity, decimal Price)? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<StatePayload>(json);
            return s is null ? null : (s.Q, s.P);
        }
        catch (JsonException) { return null; }
    }

    private sealed class StatePayload
    {
        public decimal Q { get; set; }
        public decimal P { get; set; }
    }
}
