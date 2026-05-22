using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Registry;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Errors;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.Services;

namespace Observa.Features.Connectors.Orchestration;

public sealed class ConnectorPollOrchestrator(
    IGrainFactory grains,
    IConnectorRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<ConnectorPollOrchestrator> logger)
{
    public async Task PollAsync(Guid streamId, CancellationToken ct)
    {
        var grain = grains.GetGrain<IStreamGrain>(streamId);
        var state = await grain.GetAsync();

        if (state.Binding is null)
        {
            logger.LogDebug("Stream {StreamId} has no connector binding; skipping poll.", streamId);
            return;
        }

        var connectorId = new ConnectorId(state.Binding.ConnectorId);
        var connector = registry.Find(connectorId);
        if (connector is null)
        {
            logger.LogWarning("Connector '{ConnectorId}' not registered; stream {StreamId} cannot poll.",
                state.Binding.ConnectorId, streamId);
            await grain.LogActivityAsync(new ActivityLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Kind = "PollFailed",
                Message = $"Connector '{state.Binding.ConnectorId}' is not registered.",
            });
            return;
        }

        // Record the poll time now so status (Last fired / Next fire estimate) is accurate for
        // every poll — the initial poll on registration as well as scheduled reminder fires.
        await grain.MarkPolledAsync(DateTimeOffset.UtcNow);

        await grain.LogActivityAsync(new ActivityLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = "PollStarted",
            Message = $"Polling {connector.Metadata.DisplayName}",
            Details = new Dictionary<string, string>
            {
                ["ConnectorId"] = connectorId.Value,
                ["ExternalRef"] = state.Binding.ExternalRef,
                ["Since"] = state.Binding.LastSync?.ToString("O") ?? "(initial backfill)",
            },
        });

        if (connector is ISnapshotConnector snapshotConnector)
        {
            await PollSnapshotAsync(grain, snapshotConnector, streamId, state.Binding, ct);
            return;
        }

        IReadOnlyList<ConnectorFlowEvent> fetched;
        try
        {
            var fetchContext = new ConnectorFetchContext(
                CallerId: streamId,
                ExternalRef: state.Binding.ExternalRef,
                Since: state.Binding.LastSync);
            fetched = await connector.FetchEventsAsync(fetchContext, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connector '{ConnectorId}' fetch failed for stream {StreamId}.",
                state.Binding.ConnectorId, streamId);
            await grain.LogActivityAsync(new ActivityLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Kind = "PollFailed",
                Message = $"Fetch threw: {ex.GetType().Name}: {ex.Message}",
            });
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StreamService>();

        var ingestedCount = 0;
        var duplicateCount = 0;
        foreach (var ev in fetched)
        {
            var result = await service.IngestEventAsync(
                StreamId.From(streamId),
                new IngestEventDto(ev.OccurredAt, ev.AmountUsd, IngestionSource.Connector, ev.ExternalEventId),
                ct);

            if (result.IsSuccess)
            {
                ingestedCount++;
                continue;
            }

            if (result.Errors.Any(e => e.ErrorCode == DomainErrors.FlowEvent.Duplicate))
            {
                duplicateCount++;
                continue;
            }

            logger.LogWarning("Stream {StreamId} ingest failed for external ref {ExternalRef}: {Errors}",
                streamId, ev.ExternalEventId,
                string.Join(",", result.Errors.Select(e => e.ErrorCode)));
        }

        var pollResult = await service.RecordPollAsync(StreamId.From(streamId), DateTimeOffset.UtcNow, ct);
        if (pollResult.IsFailure)
        {
            logger.LogWarning("Stream {StreamId} RecordPoll failed: {Errors}",
                streamId, string.Join(",", pollResult.Errors.Select(e => e.ErrorCode)));
        }

        await grain.LogActivityAsync(new ActivityLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = "PollCompleted",
            Message = $"Fetched {fetched.Count}, ingested {ingestedCount}, dup-skipped {duplicateCount}.",
            Details = new Dictionary<string, string>
            {
                ["Fetched"] = fetched.Count.ToString(),
                ["Ingested"] = ingestedCount.ToString(),
                ["DuplicatesSkipped"] = duplicateCount.ToString(),
            },
        });

        logger.LogInformation("Stream {StreamId} poll complete: fetched {Fetched}, ingested {Ingested}.",
            streamId, fetched.Count, ingestedCount);
    }

    private async Task PollSnapshotAsync(
        IStreamGrain grain,
        ISnapshotConnector connector,
        Guid streamId,
        ConnectorBindingState binding,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        SnapshotSample sample;
        try
        {
            sample = await connector.SampleAsync(
                new SnapshotContext(streamId, binding.ExternalRef, binding.SnapshotState), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Snapshot connector '{ConnectorId}' sample failed for stream {StreamId}.",
                binding.ConnectorId, streamId);
            await grain.LogActivityAsync(new ActivityLogEntry
            {
                Timestamp = now, Kind = "PollFailed",
                Message = $"Snapshot threw: {ex.GetType().Name}: {ex.Message}",
            });
            return;
        }

        await grain.SetConnectorSnapshotStateAsync(sample.State, sample.CapitalBasisUsd);

        var ingested = 0;
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StreamService>();

        if (sample.PerformanceDeltaUsd != 0m)
        {
            var result = await service.IngestEventAsync(
                StreamId.From(streamId),
                new IngestEventDto(now, sample.PerformanceDeltaUsd, IngestionSource.Connector,
                    $"snapshot-{now:yyyyMMddHHmmssfff}"), ct);
            if (result.IsSuccess) ingested = 1;
            else logger.LogWarning("Stream {StreamId} snapshot ingest failed: {Errors}",
                streamId, string.Join(",", result.Errors.Select(e => e.ErrorCode)));
        }

        var pollResult = await service.RecordPollAsync(StreamId.From(streamId), now, ct);
        if (pollResult.IsFailure)
            logger.LogWarning("Stream {StreamId} RecordPoll failed: {Errors}",
                streamId, string.Join(",", pollResult.Errors.Select(e => e.ErrorCode)));

        await grain.LogActivityAsync(new ActivityLogEntry
        {
            Timestamp = now, Kind = "PollCompleted",
            Message = sample.HasPrevious
                ? $"Snapshot value Δ {sample.PerformanceDeltaUsd:F2}, ingested {ingested}."
                : $"Snapshot baseline value {sample.PerformanceDeltaUsd:F2}, ingested {ingested}.",
            Details = new Dictionary<string, string>
            {
                ["HasPrevious"] = sample.HasPrevious.ToString(),
                ["ValueDeltaUsd"] = sample.PerformanceDeltaUsd.ToString("F2"),
                ["CapitalBasisUsd"] = sample.CapitalBasisUsd.ToString("F2"),
                ["Ingested"] = ingested.ToString(),
            },
        });

        logger.LogInformation("Stream {StreamId} snapshot poll complete: hasPrevious={HasPrevious}, ingested {Ingested}.",
            streamId, sample.HasPrevious, ingested);
    }
}
