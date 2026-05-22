namespace Observa.Features.Streams.Services.Views;

public sealed record MonthlyAggregateView(
    int Year,
    int Month,
    decimal Income,
    decimal Outcome,
    decimal Net,
    int EventCount,
    decimal Performance = 0m)
{
    public DateTimeOffset StartOfMonth => new(Year, Month, 1, 0, 0, 0, TimeSpan.Zero);
    public string Label => StartOfMonth.ToString("MMM yy");
}
