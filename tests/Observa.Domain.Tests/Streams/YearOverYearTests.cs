using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class YearOverYearTests
{
    private static StreamGrainState Stream(Direction dir, params (int Year, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = "S", Category = "X", Direction = dir, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(e.Year, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Manual }).ToList(),
    };

    [Fact]
    public void YearOverYear_ComputesNetAndPercentChange()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income,      (2024, 100m), (2025, 115m), (2026, 60m)),
            Stream(Direction.Outcome,     (2024,  20m), (2025,  15m), (2026, 10m)),
            Stream(Direction.Performance, (2024, 999m)),
        };

        var rows = StreamAnalyticsService.ComputeYearOverYear(states, now);

        rows.Should().HaveCount(3);
        rows[0].Year.Should().Be(2024);
        rows[0].NetUsd.Should().Be(80m);
        rows[0].ChangePctVsPrior.Should().BeNull();
        rows[0].IsPartial.Should().BeFalse();
        rows[1].NetUsd.Should().Be(100m);
        rows[1].ChangePctVsPrior.Should().BeApproximately(0.25m, 0.0001m);
        rows[2].Year.Should().Be(2026);
        rows[2].NetUsd.Should().Be(50m);
        rows[2].IsPartial.Should().BeTrue();
    }
}
