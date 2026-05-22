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
}
