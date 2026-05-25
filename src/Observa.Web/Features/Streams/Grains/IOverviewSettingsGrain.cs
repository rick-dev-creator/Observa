namespace Observa.Features.Streams.Grains;

public interface IOverviewSettingsGrain : IGrainWithStringKey
{
    Task<decimal> GetOpeningBalanceAsync();
    Task SetOpeningBalanceAsync(decimal openingBalanceUsd);
    Task<DateTimeOffset?> GetExpenseTrackingStartAsync();
    Task SetExpenseTrackingStartAsync(DateTimeOffset? start);
}
