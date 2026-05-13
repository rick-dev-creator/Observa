namespace Observa.Features.Streams.Grains;

public interface IStreamIndexGrain : IGrainWithStringKey
{
    Task AddAsync(Guid streamId);
    Task<IReadOnlyList<Guid>> GetAllAsync();
}
