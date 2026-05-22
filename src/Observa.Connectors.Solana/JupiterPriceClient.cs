using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Observa.Connectors.Solana;

/// <summary>Fetches a token's USD price by mint from the Jupiter Price API (GET /price/v2?ids={mint}).</summary>
public sealed class JupiterPriceClient(HttpClient http, ILogger<JupiterPriceClient> logger)
{
    public async Task<decimal?> GetUsdPriceAsync(string mint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mint)) return null;
        using var res = await http.GetAsync($"/price/v2?ids={Uri.EscapeDataString(mint)}", ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("Jupiter price {Mint} returned {Status}.", mint, (int)res.StatusCode);
            return null;
        }
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
        if (!data.TryGetProperty(mint, out var entry) || entry.ValueKind != JsonValueKind.Object) return null;
        if (!entry.TryGetProperty("price", out var price)) return null;
        var raw = price.ValueKind == JsonValueKind.String ? price.GetString() : price.GetRawText();
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : null;
    }
}
