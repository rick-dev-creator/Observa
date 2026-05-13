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
            return;
        }

        IReadOnlyList<ConnectorFlowEvent> fetched;
        try
        {
            var fetchContext = new ConnectorFetchContext(
                CallerId: streamId,
                ExternalRef: state.Binding.ExternalRef,
                Since: state.Binding.LastSync ?? DateTimeOffset.UtcNow.AddDays(-30));
            fetched = await connector.FetchEventsAsync(fetchContext, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connector '{ConnectorId}' fetch failed for stream {StreamId}.",
                state.Binding.ConnectorId, streamId);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StreamService>();

        var ingestedCount = 0;
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
                continue;

            logger.LogWarning("Stream {StreamId} ingest failed for external ref {ExternalRef}: {Errors}",
                streamId, ev.ExternalEventId,
                string.Join(",", result.Errors.Select(e => e.ErrorCode)));
        }

        await grain.UpdateLastSyncAsync(DateTimeOffset.UtcNow);

        logger.LogInformation("Stream {StreamId} poll complete: fetched {Fetched}, ingested {Ingested}.",
            streamId, fetched.Count, ingestedCount);
    }
}
