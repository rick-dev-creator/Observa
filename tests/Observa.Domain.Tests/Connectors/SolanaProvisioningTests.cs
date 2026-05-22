using FluentAssertions;
using Observa.Connectors.Solana;
using Observa.Features.Connectors.Solana;

namespace Observa.Domain.Tests.Connectors;

public sealed class SolanaProvisioningTests
{
    private static DiscoveredToken Tok(string mint) => new(mint, mint, 1m, 1m, 100m);

    [Fact]
    public void TokensToCreate_ExcludesAlreadyTrackedMints()
    {
        var discovered = new[] { Tok("MintA"), Tok("MintB"), Tok("MintC") };
        var existing = new HashSet<string> { "MintB" };

        var toCreate = SolanaProvisioning.TokensToCreate(discovered, existing);

        toCreate.Select(d => d.Mint).Should().BeEquivalentTo(new[] { "MintA", "MintC" });
    }
}
