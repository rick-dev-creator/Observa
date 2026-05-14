namespace Observa.Features.Streams.Services.Views;

public sealed record RetrospectiveView(
    decimal YearToDateNet,
    decimal YearToDateIncome,
    decimal YearToDateOutcome,
    decimal? PreviousYearSamePointNet,
    decimal? YoyDelta,
    BestMonthView? BestMonth,
    BestMonthView? WorstMonth);

public sealed record BestMonthView(int Year, int Month, decimal Net);
