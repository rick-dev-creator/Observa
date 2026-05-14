namespace Observa.Features.Streams.Services.Views;

public sealed record YearlyAggregateView(
    int Year,
    decimal Income,
    decimal Outcome,
    decimal Net,
    int EventCount,
    int MonthsCovered)
{
    public decimal AverageMonthlyNet => MonthsCovered > 0 ? Net / MonthsCovered : 0m;
    public string Label => Year.ToString();
}
