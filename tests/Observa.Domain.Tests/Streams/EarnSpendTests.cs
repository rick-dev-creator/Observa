using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;
using Observa.Features.Streams.Services.Views;

namespace Observa.Domain.Tests.Streams;

public sealed class EarnSpendTests
{
    private static StreamGrainState Stream(Direction dir, params (DateTimeOffset At, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = "S", Category = "X", Direction = dir, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = e.At,
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Manual }).ToList(),
    };

    [Fact]
    public void EarnSpend_BucketsByMonth_ExcludesPerformance()
    {
        var now = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income,  (new(2026,5,2,0,0,0,TimeSpan.Zero), 1000m)),
            Stream(Direction.Outcome, (new(2026,5,3,0,0,0,TimeSpan.Zero), 400m)),
            Stream(Direction.Performance, (new(2026,5,4,0,0,0,TimeSpan.Zero), 50m)),
        };

        var pts = StreamAnalyticsService.ComputeEarnSpend(states, EarnSpendGranularity.Month, periods: 3, now);

        var may = pts.Last();
        may.IncomeUsd.Should().Be(1000m);
        may.OutcomeUsd.Should().Be(400m);
        may.NetUsd.Should().Be(600m);
    }

    [Fact]
    public void EarnSpend_BucketsByDay()
    {
        var now = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income, (new(2026,5,15,9,0,0,TimeSpan.Zero), 30m), (new(2026,5,15,18,0,0,TimeSpan.Zero), 20m)),
        };

        var pts = StreamAnalyticsService.ComputeEarnSpend(states, EarnSpendGranularity.Day, periods: 7, now);

        pts.Should().HaveCount(7);
        pts.Last().IncomeUsd.Should().Be(50m);
    }
}
