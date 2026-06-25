namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class OverviewSettingsState
{
    [Id(0)] public decimal OpeningBalanceUsd { get; set; }
    [Id(1)] public DateTimeOffset? ExpenseTrackingStart { get; set; } // savings before this date is income-only (expenses untracked)
}

public sealed class OverviewSettingsGrain(
    [PersistentState("overview-settings")] IPersistentState<OverviewSettingsState> state)
    : Grain, IOverviewSettingsGrain
{
    public const string Key = "all"; // fixed well-known grain key (not a singleton instance)

    public Task<decimal> GetOpeningBalanceAsync() => Task.FromResult(state.State.OpeningBalanceUsd);

    public async Task SetOpeningBalanceAsync(decimal openingBalanceUsd)
    {
        state.State.OpeningBalanceUsd = openingBalanceUsd;
        await state.WriteStateAsync();
    }

    public Task<DateTimeOffset?> GetExpenseTrackingStartAsync() => Task.FromResult(state.State.ExpenseTrackingStart);

    public async Task SetExpenseTrackingStartAsync(DateTimeOffset? start)
    {
        state.State.ExpenseTrackingStart = start;
        await state.WriteStateAsync();
    }
}
