namespace Observa.Features.Streams.Services.Views;

public sealed record AssetHoldingView(
    Guid StreamId, string Symbol, string Category,
    decimal ValueUsd, decimal CapitalUsd, decimal ReturnUsd, decimal? ReturnPct,
    decimal Change24hUsd, decimal? Change24hPct,   // value movement over the last 24h
    decimal Change7dUsd, decimal? Change7dPct,     // value movement over the last 7 days
    IReadOnlyList<decimal> Sparkline,              // value sampled across the recent window (oldest → newest)
    bool IsClosed);                                // position effectively exited (value ≈ 0)
