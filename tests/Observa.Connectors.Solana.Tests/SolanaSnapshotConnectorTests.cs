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
    public async Task FirstPoll_EstablishesBaseline_NoDelta()
    {
        var c = Build(Lamports(2_000_000_000), Price(100m)); // q=2, p=100
        var sample = await c.SampleAsync(new SnapshotContext(Guid.NewGuid(), Mint, PreviousState: null), CancellationToken.None);

        sample.HasPrevious.Should().BeFalse();
        sample.PerformanceDeltaUsd.Should().Be(0m);
        SnapshotStateCodec.TryParse(sample.State).Should().Be((2m, 100m));
    }

    [Fact]
    public async Task SubsequentPoll_DeltaIsPrevQuantityTimesPriceChange()
    {
        // prev q=2 @ p=100; now q=5 (bought more) @ p=120 → delta = 2*(120-100)=40 (NOT counting the +3 bought)
        var prev = SnapshotStateCodec.Serialize(2m, 100m);
        var c = Build(Lamports(5_000_000_000), Price(120m));
        var sample = await c.SampleAsync(new SnapshotContext(Guid.NewGuid(), Mint, prev), CancellationToken.None);

        sample.HasPrevious.Should().BeTrue();
        sample.PerformanceDeltaUsd.Should().Be(40m);
        SnapshotStateCodec.TryParse(sample.State).Should().Be((5m, 120m));
    }

    [Fact]
    public async Task PriceFailure_PreservesPreviousState_NoEmit()
    {
        var prev = SnapshotStateCodec.Serialize(2m, 100m);
        var c = Build(Lamports(5_000_000_000), "{\"data\":{}}"); // no price → no-op preserving prev
        var sample = await c.SampleAsync(new SnapshotContext(Guid.NewGuid(), Mint, prev), CancellationToken.None);

        sample.HasPrevious.Should().BeFalse();
        sample.PerformanceDeltaUsd.Should().Be(0m);
        sample.State.Should().Be(prev);
    }
}
