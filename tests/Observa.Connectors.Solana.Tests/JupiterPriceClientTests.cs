using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Observa.Connectors.Solana.Tests;

public sealed class JupiterPriceClientTests
{
    private const string Mint = "So11111111111111111111111111111111111111112";

    [Fact]
    public async Task GetUsdPriceAsync_ParsesPrice()
    {
        // Jupiter Price v3 shape: root keyed by mint, numeric `usdPrice`.
        var json = "{\"" + Mint + "\":{\"usdPrice\":152.34,\"decimals\":9,\"blockId\":1}}";
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("https://lite-api.jup.ag") };
        var client = new JupiterPriceClient(http, NullLogger<JupiterPriceClient>.Instance);

        var price = await client.GetUsdPriceAsync(Mint, CancellationToken.None);

        price.Should().Be(152.34m);
    }

    [Fact]
    public async Task GetUsdPriceAsync_MissingMint_ReturnsNull()
    {
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("https://lite-api.jup.ag") };
        var client = new JupiterPriceClient(http, NullLogger<JupiterPriceClient>.Instance);

        (await client.GetUsdPriceAsync(Mint, CancellationToken.None)).Should().BeNull();
    }
}
