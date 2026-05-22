namespace Observa.Features.Streams.Services.Views;

public sealed record CumulativeBalancePointView(
    string Label,
    DateTimeOffset Timestamp,
    decimal Balance,            // total = stable + volatile
    bool IsProjected,
    decimal StableBalance = 0m,    // cumulative Income − Outcome
    decimal VolatileBalance = 0m); // cumulative Performance
