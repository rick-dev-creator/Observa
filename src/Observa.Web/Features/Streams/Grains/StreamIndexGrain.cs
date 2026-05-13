namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class StreamIndexState
{
    [Id(0)] public HashSet<Guid> StreamIds { get; set; } = new();
}

public sealed class StreamIndexGrain(
    [PersistentState("stream-index")] IPersistentState<StreamIndexState> state)
    : Grain, IStreamIndexGrain
{
    public const string SingletonKey = "all";

    public async Task AddAsync(Guid streamId)
    {
        if (state.State.StreamIds.Add(streamId))
            await state.WriteStateAsync();
    }

    public Task<IReadOnlyList<Guid>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(state.State.StreamIds.ToList());
}
