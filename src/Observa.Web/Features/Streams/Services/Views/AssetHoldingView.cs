namespace Observa.Features.Streams.Services.Views;

public sealed record AssetHoldingView(
    Guid StreamId, string Symbol, string Category,
    decimal ValueUsd, decimal CapitalUsd, decimal ReturnUsd, decimal? ReturnPct);
