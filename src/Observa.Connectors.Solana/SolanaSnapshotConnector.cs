using Microsoft.Extensions.Logging;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Solana;

public sealed class SolanaSnapshotConnector : ISnapshotConnector
{
    private readonly SolanaOptions _options;
    private readonly SolanaRpcClient _rpc;
    private readonly JupiterPriceClient _jupiter;
    private readonly ILogger<SolanaSnapshotConnector> _logger;

    public SolanaSnapshotConnector(SolanaOptions options, SolanaRpcClient rpc, JupiterPriceClient jupiter,
        ILogger<SolanaSnapshotConnector> logger)
    {
        if (string.IsNullOrWhiteSpace(options.Id))
            throw new InvalidOperationException("SolanaOptions.Id is required.");
        _options = options;
        _rpc = rpc;
        _jupiter = jupiter;
        _logger = logger;

        Id = new ConnectorId(options.Id);
        Metadata = new ConnectorMetadata(
            DisplayName: string.IsNullOrWhiteSpace(options.DisplayName) ? $"Solana ({options.Id})" : $"Solana — {options.DisplayName}",
            Description: "Tracks the USD value of a Solana token holding and records its capital-netted change " +
                         "(price movement on held quantity) as Performance flow events.",
            PollInterval: options.PollInterval,
            ConfigSchema:
            [
                new ConnectorConfigField("Mint", "Token mint", ConnectorConfigFieldKind.Text,
                    Description: $"Solana token mint address. Native SOL = {SolanaOptions.NativeSolMint}."),
            ]);
    }

    public ConnectorId Id { get; }
    public ConnectorMetadata Metadata { get; }

    public Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(ConnectorFetchContext context, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConnectorFlowEvent>>([]);

    public async Task<SnapshotSample> SampleAsync(SnapshotContext context, CancellationToken ct)
    {
        var mint = context.ExternalRef;
        decimal quantity, price;
        try
        {
            quantity = await _rpc.GetTokenQuantityAsync(_options.WalletAddress, mint, ct);
            var p = await _jupiter.GetUsdPriceAsync(mint, ct);
            if (p is null)
            {
                _logger.LogWarning("Solana: no price for mint {Mint}; preserving previous state.", mint);
                return new SnapshotSample(context.PreviousState ?? "", 0m, HasPrevious: false);
            }
            price = p.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Solana sample failed for mint {Mint}; preserving previous state.", mint);
            return new SnapshotSample(context.PreviousState ?? "", 0m, HasPrevious: false);
        }

        var newState = SnapshotStateCodec.Serialize(quantity, price);
        var prev = SnapshotStateCodec.TryParse(context.PreviousState);
        if (prev is null)
            return new SnapshotSample(newState, 0m, HasPrevious: false);

        // Performance = price movement on the quantity we already held. Quantity changes are capital, excluded.
        var delta = Math.Round(prev.Value.Quantity * (price - prev.Value.Price), 2);
        return new SnapshotSample(newState, delta, HasPrevious: true);
    }
}
