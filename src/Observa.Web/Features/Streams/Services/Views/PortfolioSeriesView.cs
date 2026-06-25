namespace Observa.Features.Streams.Services.Views;

// Portfolio mark-to-market value vs invested capital (DCA) over time.
// Value = cumulative Performance events; Capital = recorded cost basis (CapitalHistory).
public sealed record PortfolioSeriesView(
    IReadOnlyList<string> Months,
    IReadOnlyList<decimal> Value,
    IReadOnlyList<decimal> Capital);
