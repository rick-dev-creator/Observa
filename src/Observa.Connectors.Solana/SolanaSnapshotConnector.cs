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
        var prev = SnapshotStateCodec.TryParse(context.PreviousState);
        decimal quantity, price;
        try
        {
            quantity = await _rpc.GetTokenQuantityAsync(_options.WalletAddress, mint, ct);
            var p = await _jupiter.GetUsdPriceAsync(mint, ct);
            if (p is null)
            {
                _logger.LogWarning("Solana: no price for mint {Mint}; preserving previous state.", mint);
                return new SnapshotSample(context.PreviousState ?? "", 0m, HasPrevious: false, prev?.Capital ?? 0m);
            }
            price = p.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Solana sample failed for mint {Mint}; preserving previous state.", mint);
            return new SnapshotSample(context.PreviousState ?? "", 0m, HasPrevious: false, prev?.Capital ?? 0m);
        }

        var value = quantity * price;
        if (prev is null)
        {
            var capital0 = Math.Round(value, 2);
            return new SnapshotSample(SnapshotStateCodec.Serialize(quantity, price, capital0),
                Math.Round(value, 2), HasPrevious: false, capital0);
        }

        var prevValue = prev.Value.Quantity * prev.Value.Price;
        var valueDelta = Math.Round(value - prevValue, 2);
        var capital = Math.Round(prev.Value.Capital + (quantity - prev.Value.Quantity) * price, 2);
        return new SnapshotSample(SnapshotStateCodec.Serialize(quantity, price, capital),
            valueDelta, HasPrevious: true, capital);
    }
}
