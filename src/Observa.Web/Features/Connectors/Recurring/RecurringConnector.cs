using Observa.Connectors.Abstractions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;

namespace Observa.Features.Connectors.Recurring;

public sealed class RecurringConnector(IGrainFactory grains) : IConnector
{
    public static readonly ConnectorId ConnectorId = new("recurring");

    public ConnectorId Id => ConnectorId;

    public ConnectorMetadata Metadata { get; } = new(
        DisplayName: "Recurring (Schedule)",
        Description: "Auto-emits flow events based on the stream's Schedule and ExpectedAmount. " +
                     "Use for salaries, fixed subscriptions, and any stream whose amount and cadence are known up front.",
        PollInterval: TimeSpan.FromHours(1),
        ConfigSchema: []);

    public async Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(
        ConnectorFetchContext context,
        CancellationToken ct)
    {
        var state = await grains.GetGrain<IStreamGrain>(context.CallerId).GetAsync();

        if (state.Schedule is null || state.ExpectedAmount is null)
            return [];

        var since = context.Since ?? DateTimeOffset.UtcNow.AddDays(-30);
        var occurrences = ComputeOccurrences(state.Schedule, since, DateTimeOffset.UtcNow);
        return occurrences
            .Select(date => new ConnectorFlowEvent(
                ExternalEventId: $"scheduled-{date:yyyyMMdd}",
                OccurredAt: date,
                AmountUsd: state.ExpectedAmount.Amount))
            .ToList();
    }

    private static IEnumerable<DateTimeOffset> ComputeOccurrences(
        RecurrenceState schedule,
        DateTimeOffset since,
        DateTimeOffset until)
        => schedule.Cadence switch
        {
            Cadence.Monthly => ComputeMonthly(schedule.Anchor, since, until),
            Cadence.Weekly => ComputeFixedInterval(schedule.Anchor, weeks: 1, since, until),
            Cadence.Biweekly => ComputeFixedInterval(schedule.Anchor, weeks: 2, since, until),
            _ => [],
        };

    private static IEnumerable<DateTimeOffset> ComputeMonthly(int dayOfMonth, DateTimeOffset since, DateTimeOffset until)
    {
        var year = since.Year;
        var month = since.Month;
        while (true)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var day = Math.Min(Math.Max(dayOfMonth, 1), daysInMonth);
            var occurrence = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);

            if (occurrence > until) yield break;
            if (occurrence > since) yield return occurrence;

            month++;
            if (month > 12) { month = 1; year++; }
        }
    }

    private static IEnumerable<DateTimeOffset> ComputeFixedInterval(int isoDayOfWeek, int weeks, DateTimeOffset since, DateTimeOffset until)
    {
        var targetDow = isoDayOfWeek == 7 ? DayOfWeek.Sunday : (DayOfWeek)isoDayOfWeek;
        var current = since.Date;
        while (current.DayOfWeek != targetDow)
            current = current.AddDays(1);

        var occurrence = new DateTimeOffset(current, TimeSpan.Zero);
        while (occurrence <= until)
        {
            if (occurrence > since) yield return occurrence;
            occurrence = occurrence.AddDays(7 * weeks);
        }
    }
}
