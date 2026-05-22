using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Observa.Connectors.Solana;

/// <summary>Reads on-chain token quantity for a wallet via Solana JSON-RPC.</summary>
public sealed class SolanaRpcClient(HttpClient http, ILogger<SolanaRpcClient> logger)
{
    public async Task<decimal> GetTokenQuantityAsync(string wallet, string mint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(wallet) || string.IsNullOrWhiteSpace(mint)) return 0m;

        if (mint == SolanaOptions.NativeSolMint)
        {
            var lamports = await RpcCallAsync("getBalance", new object[] { wallet }, ct,
                root => root.GetProperty("value").GetInt64());
            return lamports / 1_000_000_000m;
        }

        return await RpcCallAsync("getTokenAccountsByOwner",
            new object[] { wallet, new { mint }, new { encoding = "jsonParsed" } }, ct,
            root =>
            {
                decimal total = 0m;
                foreach (var acc in root.GetProperty("value").EnumerateArray())
                {
                    var amt = acc.GetProperty("account").GetProperty("data").GetProperty("parsed")
                        .GetProperty("info").GetProperty("tokenAmount");
                    var s = amt.TryGetProperty("uiAmountString", out var us) ? us.GetString() : null;
                    if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) total += d;
                }
                return total;
            });
    }

    private async Task<T> RpcCallAsync<T>(string method, object[] @params, CancellationToken ct, Func<JsonElement, T> readResult)
    {
        var req = new { jsonrpc = "2.0", id = 1, method, @params };
        using var res = await http.PostAsJsonAsync("", req, ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("Solana RPC {Method} returned {Status}.", method, (int)res.StatusCode);
            throw new HttpRequestException($"Solana RPC {method} status {(int)res.StatusCode}");
        }
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("result", out var result))
            throw new InvalidOperationException("Solana RPC response missing 'result'.");
        return readResult(result);
    }
}
