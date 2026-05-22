using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Observa.Connectors.Solana.Tests;

public sealed class JupiterTokenClientTests
{
    [Fact]
    public async Task GetSymbolAsync_ParsesSymbol_FromSearchArray()
    {
        // Jupiter token v2 search returns an array; match the entry whose id == mint.
        var json = """[{"id":"MintAAA","name":"USD Coin","symbol":"USDC","decimals":6}]""";
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("https://lite-api.jup.ag") };
        var client = new JupiterTokenClient(http, NullLogger<JupiterTokenClient>.Instance);

        (await client.GetSymbolAsync("MintAAA", CancellationToken.None)).Should().Be("USDC");
    }

    [Fact]
    public async Task GetSymbolAsync_EmptyArray_ReturnsNull()
    {
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, "[]"))
            { BaseAddress = new Uri("https://lite-api.jup.ag") };
        var client = new JupiterTokenClient(http, NullLogger<JupiterTokenClient>.Instance);

        (await client.GetSymbolAsync("MintAAA", CancellationToken.None)).Should().BeNull();
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
