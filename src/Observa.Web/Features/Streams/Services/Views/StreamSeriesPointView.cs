using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

// Per-stream monthly amount carrying the dimensions the funnel pivots on:
// Category (the "Azure-billing" axis) and IsFixed (the predecible-vs-variable axis).
public sealed record StreamSeriesPointView(
    int Year,
    int Month,
    Guid StreamId,
    string StreamName,
    string Category,
    Direction Direction,
    bool IsFixed,
    decimal Amount);
