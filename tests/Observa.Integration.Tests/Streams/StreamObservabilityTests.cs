using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.Services;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Integration.Tests.Streams;

[Collection(nameof(ObservaTestClusterCollection))]
public sealed class StreamObservabilityTests(ObservaTestClusterFixture fixture)
{
    private async Task<T> WithService<T>(Func<StreamService, Task<T>> action)
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StreamService>();
        return await action(service);
    }

    private async Task<T> WithQuery<T>(Func<StreamQueryService, Task<T>> action)
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<StreamQueryService>();
        return await action(query);
    }

    [Fact]
    public async Task ActivityLog_RecordsLifecycleAndEventActivity()
    {
        var dto = new RegisterStreamDto("Salary", "Work", Direction.Income, null, 8000m);
        var registered = await WithService(s => s.RegisterAsync(dto, CancellationToken.None));
        var id = registered.Value.StreamId;

        await WithService(s => s.IngestEventAsync(id,
            new IngestEventDto(DateTimeOffset.UtcNow, 100m, IngestionSource.Manual),
            CancellationToken.None));
        await WithService(s => s.PauseAsync(id, CancellationToken.None));
        await WithService(s => s.ResumeAsync(id, CancellationToken.None));

        var activity = await WithQuery(q => q.GetActivityAsync(id, CancellationToken.None));

        activity.Should().NotBeNull();
        activity!.ActivityLog.Should().HaveCountGreaterThanOrEqualTo(4);
        activity.ActivityLog.Select(e => e.Kind).Should().Contain(["Registered", "EventIngested", "Paused", "Resumed"]);
    }

    [Fact]
    public async Task ListOperations_ReturnsRegisteredStreams()
    {
        var dto = new RegisterStreamDto("Patreon", "Content", Direction.Income, null, 400m);
        var registered = await WithService(s => s.RegisterAsync(dto, CancellationToken.None));

        var rows = await WithQuery(q => q.ListOperationsAsync(includeTerminal: false, CancellationToken.None));

        rows.Should().Contain(r => r.Id == registered.Value.StreamId.Value && r.Name == "Patreon");
    }

    [Fact]
    public async Task ListOperations_ExcludesStoppedStreamsByDefault()
    {
        var dto = new RegisterStreamDto("OldJob", "Work", Direction.Income, null, 5000m);
        var registered = await WithService(s => s.RegisterAsync(dto, CancellationToken.None));
        var id = registered.Value.StreamId;
        await WithService(s => s.StopAsync(id, CancellationToken.None));

        var withoutTerminal = await WithQuery(q => q.ListOperationsAsync(includeTerminal: false, CancellationToken.None));
        var withTerminal = await WithQuery(q => q.ListOperationsAsync(includeTerminal: true, CancellationToken.None));

        withoutTerminal.Should().NotContain(r => r.Id == id.Value);
        withTerminal.Should().Contain(r => r.Id == id.Value);
    }

    [Fact]
    public async Task ListEvents_ReturnsIngestedEventsForStream()
    {
        var dto = new RegisterStreamDto("Tips", "Side", Direction.Income, null, null);
        var registered = await WithService(s => s.RegisterAsync(dto, CancellationToken.None));
        var id = registered.Value.StreamId;

        await WithService(s => s.IngestEventAsync(id, new IngestEventDto(DateTimeOffset.UtcNow.AddMinutes(-1), 50m, IngestionSource.Manual), CancellationToken.None));
        await WithService(s => s.IngestEventAsync(id, new IngestEventDto(DateTimeOffset.UtcNow, 75m, IngestionSource.Manual), CancellationToken.None));

        var events = await WithQuery(q => q.ListEventsAsync(id, CancellationToken.None));

        events.Should().HaveCount(2);
        events.Select(e => e.Amount).Should().Contain([50m, 75m]);
    }
}
