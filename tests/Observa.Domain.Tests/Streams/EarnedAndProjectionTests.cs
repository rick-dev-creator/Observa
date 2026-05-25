using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class EarnedAndProjectionTests
{
    private static DateTimeOffset M(int year, int month) => new(year, month, 10, 0, 0, 0, TimeSpan.Zero);

    private static StreamGrainState Stream(Direction dir, params (DateTimeOffset At, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = "S", Category = "X", Direction = dir, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = e.At,
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Manual }).ToList(),
    };

    [Fact]
    public void GrossEarned_IsCumulativeIncome_IgnoresOutcome()
    {
        var now = M(2026, 3);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income, (M(2026, 1), 1000m), (M(2026, 2), 1000m), (M(2026, 3), 1000m)),
            Stream(Direction.Outcome, (M(2026, 1), 9999m)), // must be ignored
        };

        var pts = StreamAnalyticsService.ComputeGrossEarned(states, now);

        pts.Select(p => p.Value).Should().Equal(1000m, 2000m, 3000m); // cumulative, outcome ignored
    }

    [Fact]
    public void RealNetWorth_StartsAtTrackingDate_WithOpeningBalance_NetsIncomeMinusOutcome()
    {
        var now = M(2026, 3);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income, (M(2025, 12), 5000m), (M(2026, 1), 1000m), (M(2026, 2), 1000m), (M(2026, 3), 1000m)),
            Stream(Direction.Outcome, (M(2026, 1), 400m), (M(2026, 2), 400m), (M(2026, 3), 400m)),
        };

        var pts = StreamAnalyticsService.ComputeRealNetWorth(states, openingBalance: 10000m,
            trackingStart: M(2026, 1), now: now);

        // Dec-2025 income is before the tracking start ⇒ excluded. Jan: 10000 + 1000 − 400 = 10600 ...
        pts.Select(p => (p.Date.Year, p.Date.Month, p.Value))
           .Should().Equal((2026, 1, 10600m), (2026, 2, 11200m), (2026, 3, 11800m));
    }

    [Fact]
    public void RealNetWorth_NoTrackingDate_IsEmpty()
    {
        var states = new List<StreamGrainState> { Stream(Direction.Income, (M(2026, 1), 1000m)) };
        StreamAnalyticsService.ComputeRealNetWorth(states, 0m, trackingStart: null, now: M(2026, 3))
            .Should().BeEmpty();
    }

    [Fact]
    public void EarningsProjection_ScalesAverageMonthlyTrendOverHorizons()
    {
        var now = M(2026, 7);
        // last 12 complete months are Jul-2025 .. Jun-2026. Put 1000 income + 400 outcome in each of 6 of them.
        var income = new List<(DateTimeOffset, decimal)>();
        var outcome = new List<(DateTimeOffset, decimal)>();
        for (var i = 1; i <= 6; i++)
        {
            income.Add((M(2026, i), 1000m));
            outcome.Add((M(2026, i), 400m));
        }
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income, income.ToArray()),
            Stream(Direction.Outcome, outcome.ToArray()),
        };

        // No tracking date ⇒ net averaged over all 12 window months; income averaged over 12 too.
        var rows = StreamAnalyticsService.ComputeEarningsProjection(states, trackingStart: null, now: now);

        rows.Select(r => r.Label).Should().Equal("6 months", "12 months", "3 years", "5 years");
        var avgIncome = 6 * 1000m / 12m;       // 500/mo
        var avgNet = 6 * (1000m - 400m) / 12m;  // 300/mo
        rows[0].EarnedUsd.Should().Be(avgIncome * 6);
        rows[1].EarnedUsd.Should().Be(avgIncome * 12);
        rows[1].NetUsd.Should().Be(avgNet * 12);
        rows[3].Months.Should().Be(60);
    }
}
