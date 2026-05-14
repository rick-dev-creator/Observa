namespace Observa.Features.Streams.Services.Views;

public sealed record CumulativeBalancePointView(
    string Label,
    DateTimeOffset Timestamp,
    decimal Balance,
    bool IsProjected);
