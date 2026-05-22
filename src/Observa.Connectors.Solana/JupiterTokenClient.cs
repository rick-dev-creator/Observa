using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Observa.Connectors.Solana;

/// <summary>
/// Resolves a token's symbol by mint from the Jupiter token API
/// (GET /tokens/v1/token/{mint} → { "symbol": "...", "name": "...", ... }).
/// </summary>
public sealed class JupiterTokenClient(HttpClient http, ILogger<JupiterTokenClient> logger)
{
    public async Task<string?> GetSymbolAsync(string mint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mint)) return null;
        using var res = await http.GetAsync($"/tokens/v1/token/{Uri.EscapeDataString(mint)}", ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("Jupiter token {Mint} returned {Status}.", mint, (int)res.StatusCode);
            return null;
        }
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (doc.RootElement.TryGetProperty("symbol", out var sym) && sym.ValueKind == JsonValueKind.String)
        {
            var s = sym.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }
}
