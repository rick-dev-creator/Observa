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

    public const string NativeSolMint = "So11111111111111111111111111111111111111112";
}
