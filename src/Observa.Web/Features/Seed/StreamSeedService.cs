using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Domain;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.Services;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Seed;

public sealed class StreamSeedService(
    IServiceProvider sp,
    ILogger<StreamSeedService> logger,
    IHostApplicationLifetime lifetime)
    : IHostedService
{
    private static readonly DateTimeOffset SeedStart = new(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const decimal VariableSpread = 0.4m; // ±20% around expected

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => _ = Task.Run(() => SafeSeedAsync(lifetime.ApplicationStopping)));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SafeSeedAsync(CancellationToken ct)
    {
        try
        {
            await SeedAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stream seed failed.");
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), ct); // let the silo settle

        await using var scope = sp.CreateAsyncScope();
        var streams = scope.ServiceProvider.GetRequiredService<StreamService>();
        var query = scope.ServiceProvider.GetRequiredService<StreamQueryService>();

        var existing = await query.ListOperationsAsync(includeTerminal: true, ct);
        if (existing.Count > 0)
        {
            logger.LogInformation("Stream seed skipped: {Count} stream(s) already present.", existing.Count);
            return;
        }

        logger.LogInformation("Seeding 15 streams with monthly events from {Start:yyyy-MM} to now…", SeedStart);
        var rng = new Random(42);
        var seeded = 0;
        var eventsTotal = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var item in StreamSeedCatalog.Build())
        {
            var schedule = Recurrence.Create(Cadence.Monthly, item.AnchorDay, item.Variability).Match(
                r => r,
                _ => throw new InvalidOperationException("seed: invalid Recurrence"));

            var binding = ConnectorBinding.Create(
                new ConnectorId("recurring"),
                externalRef: item.Name.ToLowerInvariant().Replace(' ', '-'),
                lastSync: now).Match(
                b => b,
                _ => throw new InvalidOperationException("seed: invalid ConnectorBinding"));

            var register = await streams.RegisterAsync(new RegisterStreamDto(
                Name: item.Name,
                Category: item.Category,
                Direction: item.Direction,
                Schedule: schedule,
                ExpectedAmount: item.ExpectedAmount,
                Binding: binding), ct);

            if (register.IsFailure)
            {
                logger.LogWarning("Seed register failed for {Name}: {Errors}",
                    item.Name, string.Join(",", register.Errors.Select(e => e.ErrorCode)));
                continue;
            }

            seeded++;
            var streamId = register.Value.StreamId;
            var ingested = await SeedEventsAsync(streams, streamId, item, rng, now, ct);
            eventsTotal += ingested;
        }

        logger.LogInformation("Stream seed complete: {Streams} streams, {Events} events.", seeded, eventsTotal);
    }

    private static async Task<int> SeedEventsAsync(
        StreamService streams,
        StreamId streamId,
        StreamSeedCatalog.SeedItem item,
        Random rng,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ingested = 0;
        var date = ClampToAnchor(SeedStart, item.AnchorDay).AddHours(9);

        while (date <= now)
        {
            var amount = item.Variability == Variability.Fixed
                ? item.ExpectedAmount
                : item.ExpectedAmount + item.ExpectedAmount * VariableSpread * ((decimal)rng.NextDouble() - 0.5m);
            amount = Math.Round(Math.Max(amount, 0.01m), 2);

            var dto = new IngestEventDto(
                OccurredAt: date,
                Amount: amount,
                Source: IngestionSource.Connector,
                ExternalRef: $"scheduled-{date:yyyyMMdd}");

            var result = await streams.IngestEventAsync(streamId, dto, ct);
            if (result.IsSuccess) ingested++;

            date = date.AddMonths(1);
        }

        return ingested;
    }

    private static DateTimeOffset ClampToAnchor(DateTimeOffset baseDate, int anchorDay)
    {
        var daysInMonth = DateTime.DaysInMonth(baseDate.Year, baseDate.Month);
        var day = Math.Min(Math.Max(anchorDay, 1), daysInMonth);
        return new DateTimeOffset(baseDate.Year, baseDate.Month, day, 0, 0, 0, baseDate.Offset);
    }
}
