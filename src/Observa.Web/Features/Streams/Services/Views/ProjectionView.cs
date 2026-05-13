namespace Observa.Features.Streams.Services.Views;

public sealed record ProjectionView(
    decimal? EndOfMonth,
    decimal? ThreeMonthsAhead,
    decimal? YearEnd,
    decimal? Uncertainty,         // approximate ±range
    int? RunwayMonths,            // months you'd survive if income stopped (negative when overspending)
    string? RunwayMessage);       // "you have ~14 months runway" / "outflows exceed inflows"
