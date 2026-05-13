using Crucible.Chains.Results;
using Crucible.Domain.Results;
using Observa.Features.Streams.Aggregates;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;
using StreamsApi = Observa.Features.Streams.Aggregates.Streams;

namespace Observa.Features.Streams.Services;

public sealed class StreamService(IGrainFactory grains, IServiceProvider sp)
{
    public Task<Result<StreamRegistered>> RegisterAsync(RegisterStreamDto dto, CancellationToken ct) =>
        StreamsApi.Register(dto)
            .DispatchEvents()
            .ExecuteAsync(sp, ct)
            .Match(
                success: ev => Result<StreamRegistered>.Success(ev),
                failure: errs => Result<StreamRegistered>.Failure(errs));

    public async Task<Result<FlowEventIngested>> IngestEventAsync(StreamId id, IngestEventDto dto, CancellationToken ct)
    {
        var snapshot = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        var snap = snapshot.AsCrucibleSnapshot();
        return await StreamsApi.ReconstructAtRegister(snap)
            .IngestEvent(dto)
            .DispatchEvents()
            .ExecuteAsync(sp, ct)
            .Match(
                success: ev => Result<FlowEventIngested>.Success(ev),
                failure: errs => Result<FlowEventIngested>.Failure(errs));
    }

    public async Task<Result<StreamPaused>> PauseAsync(StreamId id, CancellationToken ct)
    {
        var snapshot = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        var snap = snapshot.AsCrucibleSnapshot();
        return await StreamsApi.ReconstructAtRegister(snap)
            .Pause()
            .DispatchEvents()
            .ExecuteAsync(sp, ct)
            .Match(
                success: ev => Result<StreamPaused>.Success(ev),
                failure: errs => Result<StreamPaused>.Failure(errs));
    }

    public async Task<Result<StreamResumed>> ResumeAsync(StreamId id, CancellationToken ct)
    {
        var snapshot = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        var snap = snapshot.AsCrucibleSnapshot();
        return await StreamsApi.ReconstructAtPause(snap)
            .Resume()
            .DispatchEvents()
            .ExecuteAsync(sp, ct)
            .Match(
                success: ev => Result<StreamResumed>.Success(ev),
                failure: errs => Result<StreamResumed>.Failure(errs));
    }

    public async Task<Result<StreamStopped>> StopAsync(StreamId id, CancellationToken ct)
    {
        var snapshot = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        var snap = snapshot.AsCrucibleSnapshot();
        return await StreamsApi.ReconstructAtRegister(snap)
            .Stop()
            .DispatchEvents()
            .ExecuteAsync(sp, ct)
            .Match(
                success: ev => Result<StreamStopped>.Success(ev),
                failure: errs => Result<StreamStopped>.Failure(errs));
    }

    public async Task<Result<StreamDeleted>> DeleteAsync(StreamId id, CancellationToken ct)
    {
        var snapshot = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        var snap = snapshot.AsCrucibleSnapshot();
        return await StreamsApi.ReconstructAtRegister(snap)
            .Delete()
            .DispatchEvents()
            .ExecuteAsync(sp, ct)
            .Match(
                success: ev => Result<StreamDeleted>.Success(ev),
                failure: errs => Result<StreamDeleted>.Failure(errs));
    }

    public async Task<Result<ConnectorPolled>> RecordPollAsync(StreamId id, DateTimeOffset at, CancellationToken ct)
    {
        var snapshot = await grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        var snap = snapshot.AsCrucibleSnapshot();
        return await StreamsApi.ReconstructAtRegister(snap)
            .RecordPoll(at)
            .DispatchEvents()
            .ExecuteAsync(sp, ct)
            .Match(
                success: ev => Result<ConnectorPolled>.Success(ev),
                failure: errs => Result<ConnectorPolled>.Failure(errs));
    }
}
