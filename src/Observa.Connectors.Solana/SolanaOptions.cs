namespace Observa.Connectors.Solana;

public sealed class SolanaOptions
{
    public const string SectionName = "Connectors:Solana";

    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string WalletAddress { get; set; } = "";
    public string RpcUrl { get; set; } = "https://api.mainnet-beta.solana.com";
    public string JupiterBaseUrl { get; set; } = "https://lite-api.jup.ag";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Minimum USD value for a token to be auto-discovered as a stream (dust filter).</summary>
    public decimal MinValueUsd { get; set; } = 10m;

    /// <summary>How often the wallet is re-scanned for new tokens to provision.</summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromHours(24);

    public const string NativeSolMint = "So11111111111111111111111111111111111111112";
}
