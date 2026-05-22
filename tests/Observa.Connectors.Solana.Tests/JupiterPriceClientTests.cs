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
        var json = "{\"data\":{\"" + Mint + "\":{\"id\":\"" + Mint + "\",\"type\":\"derivedPrice\",\"price\":\"152.34\"}}}";
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("https://api.jup.ag") };
        var client = new JupiterPriceClient(http, NullLogger<JupiterPriceClient>.Instance);

        var price = await client.GetUsdPriceAsync(Mint, CancellationToken.None);

        price.Should().Be(152.34m);
    }

    [Fact]
    public async Task GetUsdPriceAsync_MissingMint_ReturnsNull()
    {
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, """{"data":{}}"""))
            { BaseAddress = new Uri("https://api.jup.ag") };
        var client = new JupiterPriceClient(http, NullLogger<JupiterPriceClient>.Instance);

        (await client.GetUsdPriceAsync(Mint, CancellationToken.None)).Should().BeNull();
    }
}
