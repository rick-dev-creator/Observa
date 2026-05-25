namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class OverviewSettingsState
{
    [Id(0)] public decimal OpeningBalanceUsd { get; set; }
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
}
