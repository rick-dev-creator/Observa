using System.Text.Json;

namespace Observa.Connectors.Solana;

internal static class SnapshotStateCodec
{
    public static string Serialize(decimal quantity, decimal price, decimal capital) =>
        JsonSerializer.Serialize(new StatePayload { Q = quantity, P = price, Capital = capital });

    public static (decimal Quantity, decimal Price, decimal Capital)? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<StatePayload>(json);
            return s is null ? null : (s.Q, s.P, s.Capital);
        }
        catch (JsonException) { return null; }
    }

    private sealed class StatePayload { public decimal Q { get; set; } public decimal P { get; set; } public decimal Capital { get; set; } }
}
