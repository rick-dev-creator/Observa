using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Observa.Connectors.Solana.Tests;

public sealed class JupiterTokenClientTests
{
    [Fact]
    public async Task GetSymbolAsync_ParsesSymbol()
    {
        var json = """{"address":"MintAAA","name":"USD Coin","symbol":"USDC","decimals":6}""";
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("https://lite-api.jup.ag") };
        var client = new JupiterTokenClient(http, NullLogger<JupiterTokenClient>.Instance);

        (await client.GetSymbolAsync("MintAAA", CancellationToken.None)).Should().Be("USDC");
    }

    [Fact]
    public async Task GetSymbolAsync_NotFound_ReturnsNull()
    {
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.NotFound, "not found"))
            { BaseAddress = new Uri("https://lite-api.jup.ag") };
        var client = new JupiterTokenClient(http, NullLogger<JupiterTokenClient>.Instance);

        (await client.GetSymbolAsync("MintAAA", CancellationToken.None)).Should().BeNull();
    }
}
