using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.Services;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Integration.Tests.Streams;

[Collection(nameof(ObservaTestClusterCollection))]
public sealed class StreamServiceIntegrationTests(ObservaTestClusterFixture fixture)
{
    private static RegisterStreamDto ValidDto(decimal? expected = 8000m, Recurrence? schedule = null) =>
        new("Salary", "Work", Direction.Income, schedule, expected);

    private IGrainFactory Grains => fixture.Cluster.Client;

    private async Task<T> WithService<T>(Func<StreamService, Task<T>> action)
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StreamService>();
        return await action(service);
    }

    [Fact]
    public async Task Register_PersistsStateInGrain()
    {
        var result = await WithService(s => s.RegisterAsync(ValidDto(), CancellationToken.None));

        result.IsSuccess.Should().BeTrue();
        var id = result.Value.StreamId;

        var state = await Grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        state.Name.Should().Be("Salary");
        state.Category.Should().Be("Work");
        state.Direction.Should().Be(Direction.Income);
        state.Status.Should().Be(StreamStatus.Active);
        state.ExpectedAmount!.Amount.Should().Be(8000m);
    }

    [Fact]
    public async Task Register_WithInvalidDto_ReturnsFailureAndPersistsNothing()
    {
        var result = await WithService(s => s.RegisterAsync(
            new RegisterStreamDto("", "", Direction.Income, null, null),
            CancellationToken.None));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.ErrorCode == "STREAM_NAME_REQUIRED");
    }

    [Fact]
    public async Task FullLifecycle_RegisterIngestPauseResumeIngestStop_PersistsCorrectly()
    {
        var register = await WithService(s => s.RegisterAsync(ValidDto(), CancellationToken.None));
        var id = register.Value.StreamId;

        var ingest1 = await WithService(s => s.IngestEventAsync(id,
            new IngestEventDto(DateTimeOffset.UtcNow, 100m, IngestionSource.Manual),
            CancellationToken.None));
        ingest1.IsSuccess.Should().BeTrue();

        var pause = await WithService(s => s.PauseAsync(id, CancellationToken.None));
        pause.IsSuccess.Should().BeTrue();

        var ingestWhilePaused = await WithService(s => s.IngestEventAsync(id,
            new IngestEventDto(DateTimeOffset.UtcNow, 200m, IngestionSource.Manual),
            CancellationToken.None));
        ingestWhilePaused.IsFailure.Should().BeTrue();
        ingestWhilePaused.Errors.Should().Contain(e => e.ErrorCode == "STREAM_NOT_ACTIVE");

        var resume = await WithService(s => s.ResumeAsync(id, CancellationToken.None));
        resume.IsSuccess.Should().BeTrue();

        var ingest2 = await WithService(s => s.IngestEventAsync(id,
            new IngestEventDto(DateTimeOffset.UtcNow, 300m, IngestionSource.Manual),
            CancellationToken.None));
        ingest2.IsSuccess.Should().BeTrue();

        var stop = await WithService(s => s.StopAsync(id, CancellationToken.None));
        stop.IsSuccess.Should().BeTrue();

        var state = await Grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        state.Status.Should().Be(StreamStatus.Stopped);
        state.Events.Should().HaveCount(2);
        state.Events.Select(e => e.Amount.Amount).Should().Equal(100m, 300m);
    }

    [Fact]
    public async Task Stop_OnStoppedStream_ReturnsAlreadyTerminalError()
    {
        var register = await WithService(s => s.RegisterAsync(ValidDto(), CancellationToken.None));
        var id = register.Value.StreamId;

        await WithService(s => s.StopAsync(id, CancellationToken.None));
        var second = await WithService(s => s.StopAsync(id, CancellationToken.None));

        second.IsFailure.Should().BeTrue();
        second.Errors.Should().Contain(e => e.ErrorCode == "STREAM_ALREADY_TERMINAL");
    }

    [Fact]
    public async Task Delete_OnPausedStream_TransitionsToDeleted()
    {
        var register = await WithService(s => s.RegisterAsync(ValidDto(), CancellationToken.None));
        var id = register.Value.StreamId;

        await WithService(s => s.PauseAsync(id, CancellationToken.None));
        var delete = await WithService(s => s.DeleteAsync(id, CancellationToken.None));

        delete.IsSuccess.Should().BeTrue();
        var state = await Grains.GetGrain<IStreamGrain>(id.Value).GetAsync();
        state.Status.Should().Be(StreamStatus.Deleted);
    }
}

[CollectionDefinition(nameof(ObservaTestClusterCollection))]
public sealed class ObservaTestClusterCollection : ICollectionFixture<ObservaTestClusterFixture>;
