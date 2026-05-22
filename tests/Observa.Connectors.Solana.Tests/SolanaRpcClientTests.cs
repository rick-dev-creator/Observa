using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Observa.Connectors.Solana.Tests;

public sealed class SolanaRpcClientTests
{
    private const string Wallet = "Wa11et1111111111111111111111111111111111111";
    private const string NativeSol = "So11111111111111111111111111111111111111112";

    [Fact]
    public async Task GetTokenQuantity_NativeSol_UsesLamports()
    {
        var json = """{"jsonrpc":"2.0","result":{"context":{"slot":1},"value":2500000000},"id":1}""";
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("https://rpc.test") };
        var client = new SolanaRpcClient(http, NullLogger<SolanaRpcClient>.Instance);

        var qty = await client.GetTokenQuantityAsync(Wallet, NativeSol, CancellationToken.None);

        qty.Should().Be(2.5m); // 2_500_000_000 lamports / 1e9
    }

    [Fact]
    public async Task GetTokenQuantity_SplToken_SumsUiAmount()
    {
        var json = """
        {"jsonrpc":"2.0","result":{"context":{"slot":1},"value":[
          {"account":{"data":{"parsed":{"info":{"tokenAmount":{"uiAmountString":"100.5","uiAmount":100.5,"decimals":6,"amount":"100500000"}}}}}},
          {"account":{"data":{"parsed":{"info":{"tokenAmount":{"uiAmountString":"0.5","uiAmount":0.5,"decimals":6,"amount":"500000"}}}}}}
        ]},"id":1}
        """;
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, json))
            { BaseAddress = new Uri("https://rpc.test") };
        var client = new SolanaRpcClient(http, NullLogger<SolanaRpcClient>.Instance);

        var qty = await client.GetTokenQuantityAsync(Wallet, "SomeMint11111111111111111111111111111111111", CancellationToken.None);

        qty.Should().Be(101.0m);
    }
}
