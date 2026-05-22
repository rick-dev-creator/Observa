using Microsoft.Extensions.Logging;

namespace Observa.Connectors.Solana;

public sealed record DiscoveredToken(string Mint, string Symbol, decimal Quantity, decimal PriceUsd, decimal ValueUsd);

/// <summary>Discovers a wallet's tokens worth >= a USD threshold, resolving symbols for naming.</summary>
public sealed class SolanaWalletScanner(
    SolanaRpcClient rpc,
    JupiterPriceClient price,
    JupiterTokenClient tokens,
    ILogger<SolanaWalletScanner> logger)
{
    public async Task<IReadOnlyList<DiscoveredToken>> ScanAsync(string wallet, decimal minValueUsd, CancellationToken ct)
    {
        var holdings = await rpc.GetHoldingsAsync(wallet, ct);
        var found = new List<DiscoveredToken>();
        foreach (var (mint, qty) in holdings)
        {
            var p = await price.GetUsdPriceAsync(mint, ct);
            if (p is null) continue;                  // can't value → skip (illiquid/unknown)
            var value = qty * p.Value;
            if (value < minValueUsd) continue;         // dust filter
            var symbol = await tokens.GetSymbolAsync(mint, ct) ?? ShortMint(mint);
            found.Add(new DiscoveredToken(mint, symbol, qty, p.Value, Math.Round(value, 2)));
        }
        logger.LogInformation("Solana scan {Wallet}: {Kept} token(s) >= ${MinUsd}.", wallet, found.Count, minValueUsd);
        return found;
    }

    private static string ShortMint(string mint) =>
        mint.Length <= 9 ? mint : $"{mint[..4]}…{mint[^4..]}";
}
