using Observa.Connectors.Solana;

namespace Observa.Features.Connectors.Solana;

internal static class SolanaProvisioning
{
    /// <summary>Discovered tokens whose mint is not already tracked (by an existing non-terminal binding).</summary>
    public static IReadOnlyList<DiscoveredToken> TokensToCreate(
        IReadOnlyList<DiscoveredToken> discovered, ISet<string> existingMints) =>
        discovered.Where(d => !existingMints.Contains(d.Mint)).ToList();
}
