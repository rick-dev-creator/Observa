using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Solana;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSolanaConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var accounts = configuration.GetSection(SolanaOptions.SectionName).Get<SolanaOptions[]>() ?? [];

        if (accounts.Length == 0) return services;

        // All accounts share one RPC/Jupiter HttpClient using the first account's URLs.
        // Multi-account with different RPC endpoints is not supported (same limitation as Blofin).
        services.AddHttpClient<SolanaRpcClient>(http =>
        {
            http.BaseAddress = new Uri(accounts.FirstOrDefault()?.RpcUrl ?? "https://api.mainnet-beta.solana.com");
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<JupiterPriceClient>(http =>
        {
            http.BaseAddress = new Uri(accounts.FirstOrDefault()?.JupiterBaseUrl ?? "https://lite-api.jup.ag");
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<JupiterTokenClient>(http =>
        {
            http.BaseAddress = new Uri(accounts.FirstOrDefault()?.JupiterBaseUrl ?? "https://lite-api.jup.ag");
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<SolanaWalletScanner>();

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var options in accounts)
        {
            if (string.IsNullOrWhiteSpace(options.Id))
                throw new InvalidOperationException("Solana account configuration is missing required 'Id'.");
            if (!seenIds.Add(options.Id))
                throw new InvalidOperationException($"Solana account id '{options.Id}' is duplicated in configuration.");
            if (string.IsNullOrWhiteSpace(options.WalletAddress)) continue;

            services.AddSingleton<IConnector>(sp => new SolanaSnapshotConnector(
                options,
                sp.GetRequiredService<SolanaRpcClient>(),
                sp.GetRequiredService<JupiterPriceClient>(),
                sp.GetRequiredService<ILogger<SolanaSnapshotConnector>>()));
        }
        return services;
    }
}
