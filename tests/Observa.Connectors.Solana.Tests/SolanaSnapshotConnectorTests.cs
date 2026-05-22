using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Solana.Tests;

public sealed class SolanaSnapshotConnectorTests
{
    private const string Mint = "So11111111111111111111111111111111111111112";

    private static SolanaSnapshotConnector Build(string rpcJson, string jupiterJson)
    {
        var rpc = new SolanaRpcClient(
            new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, rpcJson)) { BaseAddress = new Uri("https://rpc.test") },
            NullLogger<SolanaRpcClient>.Instance);
        var jup = new JupiterPriceClient(
            new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, jupiterJson)) { BaseAddress = new Uri("https://api.jup.ag") },
            NullLogger<JupiterPriceClient>.Instance);
        var options = new SolanaOptions { Id = "solana-main", WalletAddress = "Wa11et", DisplayName = "Main" };
        return new SolanaSnapshotConnector(options, rpc, jup, NullLogger<SolanaSnapshotConnector>.Instance);
    }

    private static string Lamports(long v) => "{\"jsonrpc\":\"2.0\",\"result\":{\"value\":" + v + "},\"id\":1}";
    private static string Price(decimal p) => "{\"" + Mint + "\":{\"usdPrice\":" + p.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";

    [Fact]
    public async Task FirstPoll_EmitsFullValue_AsBaseline()
    {
        var c = Build(Lamports(2_000_000_000), Price(100m)); // q=2, p=100 → value 200
        var s = await c.SampleAsync(new SnapshotContext(Guid.NewGuid(), Mint, null), CancellationToken.None);
        s.PerformanceDeltaUsd.Should().Be(200m);
        s.CapitalBasisUsd.Should().Be(200m);
        s.HasPrevious.Should().BeFalse();
        SnapshotStateCodec.TryParse(s.State).Should().Be((2m, 100m, 200m));
    }

    [Fact]
    public async Task PriceRises_SameQty_ValueDeltaIsPriceMove_CapitalUnchanged()
    {
        var prev = SnapshotStateCodec.Serialize(2m, 100m, 200m);
        var c = Build(Lamports(2_000_000_000), Price(120m)); // value 240
        var s = await c.SampleAsync(new SnapshotContext(Guid.NewGuid(), Mint, prev), CancellationToken.None);
        s.PerformanceDeltaUsd.Should().Be(40m);
        s.CapitalBasisUsd.Should().Be(200m);
        s.HasPrevious.Should().BeTrue();
    }

    [Fact]
    public async Task BuyMore_PriceFlat_ValueDeltaIsPurchase_CapitalIncreases()
    {
        var prev = SnapshotStateCodec.Serialize(2m, 100m, 200m);
        var c = Build(Lamports(5_000_000_000), Price(100m)); // q=5 (+3), value 500
        var s = await c.SampleAsync(new SnapshotContext(Guid.NewGuid(), Mint, prev), CancellationToken.None);
        s.PerformanceDeltaUsd.Should().Be(300m);   // 500 − 200
        s.CapitalBasisUsd.Should().Be(500m);        // 200 + 3·100
    }

    [Fact]
    public async Task PriceUnavailable_PreservesPreviousState_NoEmit_KeepsCapital()
    {
        var prev = SnapshotStateCodec.Serialize(2m, 100m, 200m);
        var c = Build(Lamports(5_000_000_000), "{}"); // empty Jupiter → no price
        var s = await c.SampleAsync(new SnapshotContext(Guid.NewGuid(), Mint, prev), CancellationToken.None);
        s.HasPrevious.Should().BeFalse();
        s.PerformanceDeltaUsd.Should().Be(0m);
        s.State.Should().Be(prev);          // previous state preserved unchanged
        s.CapitalBasisUsd.Should().Be(200m); // previous capital preserved
    }
}
