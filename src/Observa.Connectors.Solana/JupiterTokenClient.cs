using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Observa.Connectors.Solana;

/// <summary>
/// Resolves a token's symbol by mint from the Jupiter token API
/// (GET /tokens/v2/search?query={mint} → [ { "id": "&lt;mint&gt;", "symbol": "...", "name": "..." }, … ]).
/// </summary>
public sealed class JupiterTokenClient(HttpClient http, ILogger<JupiterTokenClient> logger)
{
    public async Task<string?> GetSymbolAsync(string mint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mint)) return null;
        using var res = await http.GetAsync($"/tokens/v2/search?query={Uri.EscapeDataString(mint)}", ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("Jupiter token {Mint} returned {Status}.", mint, (int)res.StatusCode);
            return null;
        }
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        // Search returns matches; pick the entry whose id == mint (fall back to the first), read its symbol.
        JsonElement? chosen = null;
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            chosen ??= entry;
            if (entry.TryGetProperty("id", out var id) && id.GetString() == mint) { chosen = entry; break; }
        }
        if (chosen is not { } match) return null;
        if (match.TryGetProperty("symbol", out var sym) && sym.ValueKind == JsonValueKind.String)
        {
            var s = sym.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }
}
