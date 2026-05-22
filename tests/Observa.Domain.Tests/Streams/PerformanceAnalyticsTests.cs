using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class PerformanceAnalyticsTests
{
    [Theory]
    [InlineData(Direction.Income, 100, 100)]
    [InlineData(Direction.Outcome, 100, -100)]
    [InlineData(Direction.Performance, 100, 100)]
    [InlineData(Direction.Performance, -100, -100)]
    public void SignedNet_AppliesDirectionSign(Direction d, decimal amount, decimal expected)
    {
        StreamAnalyticsService.SignedNet(d, amount).Should().Be(expected);
    }

    [Fact]
    public void ComputeCurrentMonth_NetMTD_IncludesPerformance()
    {
        // Build events dated within the current month
        var now = DateTimeOffset.UtcNow;
        var thisMonthEvent = new DateTimeOffset(now.Year, now.Month, now.Day, 12, 0, 0, TimeSpan.Zero);

        var incomeStream = new StreamGrainState
        {
            Id = Guid.NewGuid(),
            Direction = Direction.Income,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                new() { Id = Guid.NewGuid(), OccurredAt = thisMonthEvent, Amount = new MoneyState { Amount = 1000m } },
            },
        };

        var performanceStream = new StreamGrainState
        {
            Id = Guid.NewGuid(),
            Direction = Direction.Performance,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                // Signed negative (loss) — stored as-is per Task 3 convention
                new() { Id = Guid.NewGuid(), OccurredAt = thisMonthEvent, Amount = new MoneyState { Amount = -300m } },
            },
        };

        var states = new List<StreamGrainState> { incomeStream, performanceStream };

        var result = StreamAnalyticsService.ComputeCurrentMonth(states);

        result.IncomeMTD.Should().Be(1000m);
        result.PerformanceMTD.Should().Be(-300m);
        // Net = Income(1000) - Outcome(0) + Performance(-300) = 700
        result.NetMTD.Should().Be(700m);
    }

    [Fact]
    public void ComputeMonthlyHistory_IncludesPerformanceInNet()
    {
        // Use last month (complete — not the current partial month) so we have a full bucket.
        var now = DateTimeOffset.UtcNow;
        var lastMonthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1);
        var eventTs = lastMonthStart.AddDays(5);

        var incomeStream = new StreamGrainState
        {
            Id = Guid.NewGuid(),
            Direction = Direction.Income,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                new() { Id = Guid.NewGuid(), OccurredAt = eventTs, Amount = new MoneyState { Amount = 1000m } },
            },
        };

        var performanceStream = new StreamGrainState
        {
            Id = Guid.NewGuid(),
            Direction = Direction.Performance,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                // Signed negative (loss) — stored as-is per Task 3 convention
                new() { Id = Guid.NewGuid(), OccurredAt = eventTs, Amount = new MoneyState { Amount = -300m } },
            },
        };

        var states = new List<StreamGrainState> { incomeStream, performanceStream };

        // Request 12 months so last month is within the window
        var history = StreamAnalyticsService.ComputeMonthlyHistory(states, 12);

        var bucket = history.FirstOrDefault(m => m.Year == lastMonthStart.Year && m.Month == lastMonthStart.Month);
        bucket.Should().NotBeNull("last month bucket should exist in 12-month history");
        bucket!.Income.Should().Be(1000m);
        bucket.Performance.Should().Be(-300m);
        // Net = Income(1000) - Outcome(0) + Performance(-300) = 700
        bucket.Net.Should().Be(700m);
    }

    [Fact]
    public void ComputeStreamTrends_Performance_RecentAverage_IncludesNegativeMonths()
    {
        // Arrange: a Performance stream with three months: -500, -300, +800.
        // RecentAverage should include all three non-zero months: (-500 + -300 + 800) / 3 = 0.
        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var m1 = anchor.AddMonths(-3).AddDays(5);
        var m2 = anchor.AddMonths(-2).AddDays(5);
        var m3 = anchor.AddMonths(-1).AddDays(5);

        var performanceStream = new StreamGrainState
        {
            Id = Guid.NewGuid(),
            Direction = Direction.Performance,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                new() { Id = Guid.NewGuid(), OccurredAt = m1, Amount = new MoneyState { Amount = -500m } },
                new() { Id = Guid.NewGuid(), OccurredAt = m2, Amount = new MoneyState { Amount = -300m } },
                new() { Id = Guid.NewGuid(), OccurredAt = m3, Amount = new MoneyState { Amount = 800m } },
            },
        };

        var states = new List<StreamGrainState> { performanceStream };

        // Act: use 4 sparkline months so months -3..-1 are visible (month -4 bucket will be 0).
        var trends = StreamAnalyticsService.ComputeStreamTrends(states, 4);

        var trend = trends.Should().ContainSingle().Subject;
        // All three non-zero months are included: (-500 + -300 + 800) / 3 = 0
        trend.RecentAverage.Should().Be(0m,
            "Performance RecentAverage must include negative months, not filter them out");
    }

    [Fact]
    public void ApplyScenario_Performance_PreservesLossSign()
    {
        // Arrange: a Performance stream with a loss event (-400).
        // ApplyScenario with factor 2.0 should yield -800, NOT clamped to 0.
        var now = DateTimeOffset.UtcNow;
        var eventTs = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(5);

        var streamId = Guid.NewGuid();
        var performanceStream = new StreamGrainState
        {
            Id = streamId,
            Direction = Direction.Performance,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                new() { Id = Guid.NewGuid(), OccurredAt = eventTs, Amount = new MoneyState { Amount = -400m } },
            },
        };

        var scenario = new Scenario(
            Id: "test-double",
            Label: "Double",
            Narrative: "Test scenario",
            ExcludedStreamIds: new HashSet<Guid>(),
            StreamMultipliers: new Dictionary<Guid, decimal> { [streamId] = 2m },
            CategoryMultipliers: new Dictionary<string, decimal>(),
            DirectionMultipliers: new Dictionary<Direction, decimal>());

        var states = new List<StreamGrainState> { performanceStream };

        // Act
        var result = StreamAnalyticsService.ApplyScenario(states, scenario);

        result.Should().HaveCount(1);
        var scaledEvent = result[0].Events.Should().ContainSingle().Subject;
        scaledEvent.Amount.Amount.Should().Be(-800m,
            "ApplyScenario must preserve the sign of Performance losses (not clamp to 0)");
    }

    [Fact]
    public void ComputeProjection_Uncertainty_Is1Point65Sigma()
    {
        // Arrange: build a Performance stream with events in three complete past months,
        // plus a steady Income stream. The volatile Performance events make Net vary so
        // stddev > 0 and the 1.65σ band is meaningful.
        var now = DateTimeOffset.UtcNow;

        // Months -3, -2, -1 are all complete (current month is excluded from completeMonths).
        var month3Start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-3);
        var month2Start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-2);
        var month1Start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1);

        // Steady income: 2000 each month in the three complete past months.
        var incomeStream = new StreamGrainState
        {
            Id = Guid.NewGuid(),
            Direction = Direction.Income,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                new() { Id = Guid.NewGuid(), OccurredAt = month3Start.AddDays(5), Amount = new MoneyState { Amount = 2000m } },
                new() { Id = Guid.NewGuid(), OccurredAt = month2Start.AddDays(5), Amount = new MoneyState { Amount = 2000m } },
                new() { Id = Guid.NewGuid(), OccurredAt = month1Start.AddDays(5), Amount = new MoneyState { Amount = 2000m } },
            },
        };

        // Volatile performance: +500, -300, +800 across the three months (signed, stored as-is).
        var performanceStream = new StreamGrainState
        {
            Id = Guid.NewGuid(),
            Direction = Direction.Performance,
            Status = StreamStatus.Active,
            Events = new List<FlowEventSnapshot>
            {
                new() { Id = Guid.NewGuid(), OccurredAt = month3Start.AddDays(10), Amount = new MoneyState { Amount = 500m } },
                new() { Id = Guid.NewGuid(), OccurredAt = month2Start.AddDays(10), Amount = new MoneyState { Amount = -300m } },
                new() { Id = Guid.NewGuid(), OccurredAt = month1Start.AddDays(10), Amount = new MoneyState { Amount = 800m } },
            },
        };

        var states = new List<StreamGrainState> { incomeStream, performanceStream };

        // Act
        var result = StreamAnalyticsService.ComputeProjection(states);

        // Derive the expected band from the same data ComputeProjection uses:
        // completeMonths = ComputeMonthlyHistory(states, 12).SkipLast(1)
        // (current partial month is excluded — SkipLast(1) matches the service logic)
        var history = StreamAnalyticsService.ComputeMonthlyHistory(states, 12);
        var completeMonths = history.SkipLast(1).ToArray();
        var nets = completeMonths.Select(m => m.Net).ToArray();

        // Replicate the private StdDev formula (sample variance, divides by n-1)
        static decimal SampleStdDev(decimal[] values)
        {
            if (values.Length < 2) return 0m;
            var mean = values.Average();
            var variance = values.Select(v => (v - mean) * (v - mean)).Sum() / (values.Length - 1);
            return (decimal)Math.Sqrt((double)variance);
        }

        var stddev = SampleStdDev(nets);
        const decimal BandSigma = 1.65m;
        var expectedUncertainty = Math.Round(BandSigma * stddev, 2);

        // Assert: Uncertainty must equal 1.65σ (not 1σ)
        result.Uncertainty.Should().Be(expectedUncertainty,
            "the projection band should be ≈P5–P95 (1.65σ) so volatile assets read as uncertain");

        // Guard: the band must actually be wider than 1σ (confirms 1.65 multiplier applies)
        result.Uncertainty.Should().BeGreaterThan(Math.Round(stddev, 2),
            "1.65σ must be strictly wider than 1σ when stddev > 0");
    }
}
