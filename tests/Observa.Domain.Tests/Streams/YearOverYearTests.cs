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
            Stream(Direction.Income, (2024, 100m), (2025, 115m), (2026, 60m)),
            Stream(Direction.Outcome, (2024, 0m), (2025, 0m), (2026, 0m)),
        };

        var rows = StreamAnalyticsService.ComputeYearOverYear(states, now);

        rows.Should().HaveCount(3);
        rows[0].Should().BeEquivalentTo(new { Year = 2024, NetUsd = 100m, ChangePctVsPrior = (decimal?)null, IsPartial = false });
        rows[1].ChangePctVsPrior.Should().BeApproximately(0.15m, 0.0001m);
        rows[2].IsPartial.Should().BeTrue();
    }
}
