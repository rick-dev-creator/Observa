namespace Observa.Features.Streams.Services.Views;

public enum EarnSpendGranularity { Day, Week, Month, Year }

public sealed record EarnSpendPointView(string Label, DateTimeOffset BucketStart, decimal IncomeUsd, decimal OutcomeUsd)
{
    public decimal NetUsd => IncomeUsd - OutcomeUsd;
}
