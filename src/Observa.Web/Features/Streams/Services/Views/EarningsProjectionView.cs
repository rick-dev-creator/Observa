namespace Observa.Features.Streams.Services.Views;

// Projected gross earnings and net savings over a horizon, at the current trend.
public sealed record EarningsProjectionRowView(string Label, int Months, decimal EarnedUsd, decimal NetUsd);
