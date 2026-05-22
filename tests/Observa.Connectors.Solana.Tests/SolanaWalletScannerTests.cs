using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Observa.Connectors.Solana.Tests;

public sealed class SolanaWalletScannerTests
{
    private const string Sol = "So11111111111111111111111111111111111111112";

    [Fact]
    public async Task ScanAsync_FiltersByThreshold_AndResolvesSymbols()
    {
        // Holdings: SOL 2.0 @ $100 = $200 (keep); MintLOW 5 @ $1 = $5 (drop, < $10).
        var rpc = new HttpClient(new RoutingStubHttpMessageHandler()
            .Add((u, b) => b.Contains("getBalance"), """{"jsonrpc":"2.0","result":{"value":2000000000},"id":1}""")
            .Add((u, b) => b.Contains("getTokenAccountsByOwner"),
                 """{"jsonrpc":"2.0","result":{"value":[{"account":{"data":{"parsed":{"info":{"mint":"MintLOW","tokenAmount":{"uiAmountString":"5"}}}}}}]},"id":1}""")) { BaseAddress = new Uri("https://rpc.test") };

        var jup = new HttpClient(new RoutingStubHttpMessageHandler()
            .Add((u, b) => u.Contains("/price/v3") && u.Contains(Sol), "{\"" + Sol + "\":{\"usdPrice\":100}}")
            .Add((u, b) => u.Contains("/price/v3") && u.Contains("MintLOW"), "{\"MintLOW\":{\"usdPrice\":1}}")
            .Add((u, b) => u.Contains("/tokens/") && u.Contains(Sol), """{"symbol":"SOL"}""")
            .Add((u, b) => u.Contains("/tokens/") && u.Contains("MintLOW"), """{"symbol":"LOW"}"""))
            { BaseAddress = new Uri("https://lite-api.jup.ag") };

        var scanner = new SolanaWalletScanner(
            new SolanaRpcClient(rpc, NullLogger<SolanaRpcClient>.Instance),
            new JupiterPriceClient(jup, NullLogger<JupiterPriceClient>.Instance),
            new JupiterTokenClient(jup, NullLogger<JupiterTokenClient>.Instance),
            NullLogger<SolanaWalletScanner>.Instance);

        var found = await scanner.ScanAsync("Wa11et", minValueUsd: 10m, CancellationToken.None);

        found.Should().ContainSingle();
        found[0].Mint.Should().Be(Sol);
        found[0].Symbol.Should().Be("SOL");
        found[0].ValueUsd.Should().Be(200m);
    }
}
