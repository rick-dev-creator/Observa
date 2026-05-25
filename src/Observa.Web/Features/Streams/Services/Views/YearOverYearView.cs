namespace Observa.Features.Streams.Services.Views;

public sealed record YearOverYearView(
    int Year,
    decimal NetUsd,                 // Σ Income − Σ Outcome for the year (cash flow earned/saved)
    decimal? ChangePctVsPrior,      // fraction vs previous year's net; null for the first year
    bool IsPartial);                // true for the current (incomplete) year
