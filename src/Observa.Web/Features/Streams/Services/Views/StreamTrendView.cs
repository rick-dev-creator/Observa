using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

public sealed record StreamTrendView(
    Guid Id,
    string Name,
    string Category,
    Direction Direction,
    StreamStatus Status,
    decimal? ExpectedAmount,
    decimal? LastMonthAmount,
    decimal? RecentAverage,           // last 6 months
    decimal? Slope,                    // per-month $ change
    string TrendLabel,                 // "Steady" / "Trending up" / "Trending down" / "Volatile" / "Insufficient data"
    string TrendDetail,                // "+$280/month" / "varies a lot" / etc
    IReadOnlyList<decimal> Sparkline); // last 12 months
