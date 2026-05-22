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

    private const string TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";

    /// <summary>All holdings (native SOL + SPL tokens with quantity &gt; 0) for a wallet.</summary>
    public async Task<IReadOnlyList<(string Mint, decimal Quantity)>> GetHoldingsAsync(string wallet, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(wallet)) return [];
        var holdings = new List<(string, decimal)>();

        var lamports = await RpcCallAsync("getBalance", new object[] { wallet }, ct,
            root => root.GetProperty("value").GetInt64());
        var sol = lamports / 1_000_000_000m;
        if (sol > 0) holdings.Add((SolanaOptions.NativeSolMint, sol));

        var spl = await RpcCallAsync("getTokenAccountsByOwner",
            new object[] { wallet, new { programId = TokenProgram }, new { encoding = "jsonParsed" } }, ct,
            root =>
            {
                var list = new List<(string, decimal)>();
                foreach (var acc in root.GetProperty("value").EnumerateArray())
                {
                    var info = acc.GetProperty("account").GetProperty("data").GetProperty("parsed").GetProperty("info");
                    var mint = info.TryGetProperty("mint", out var m) ? m.GetString() : null;
                    var ui = info.TryGetProperty("tokenAmount", out var ta) && ta.TryGetProperty("uiAmountString", out var us)
                        ? us.GetString() : null;
                    if (mint is not null
                        && decimal.TryParse(ui, NumberStyles.Any, CultureInfo.InvariantCulture, out var q) && q > 0)
                        list.Add((mint, q));
                }
                return list;
            });
        holdings.AddRange(spl);
        return holdings;
    }

    private async Task<T> RpcCallAsync<T>(string method, object[] @params, CancellationToken ct, Func<JsonElement, T> readResult)
    {
        var req = new { jsonrpc = "2.0", id = 1, method, @params };

        // Public RPCs rate-limit (429). One short backoff-and-retry handles transient throttling.
        for (var attempt = 0; ; attempt++)
        {
            using var res = await http.PostAsJsonAsync("", req, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt == 0)
            {
                logger.LogWarning("Solana RPC {Method} rate-limited (429); retrying once.", method);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }
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
}
