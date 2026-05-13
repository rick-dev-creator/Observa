namespace Observa.Features.Streams.Services.Views;

public sealed record MonthSummaryView(
    int Year,
    int Month,
    decimal IncomeMTD,
    decimal OutcomeMTD,
    decimal NetMTD,
    decimal? OnTrackEom,                  // linear extrapolation
    decimal? PreviousMonthSamePoint,      // net at same day-into-month last month
    decimal? Delta,                        // NetMTD - PreviousMonthSamePoint
    int DaysIntoMonth,
    int DaysInMonth);
