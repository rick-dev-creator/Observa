using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class NetWorthTrajectoryTests
{
    private static DateTimeOffset M(int year, int month) => new(year, month, 10, 0, 0, 0, TimeSpan.Zero);

    private static StreamGrainState Income(string name, params (DateTimeOffset At, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = name, Category = "X", Direction = Direction.Income, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = e.At,
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Connector }).ToList(),
    };

    private static StreamGrainState Asset(string name, decimal capital, List<CapitalPoint> capHist,
        params (DateTimeOffset At, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = name, Category = "Crypto", Direction = Direction.Performance, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = e.At,
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Connector }).ToList(),
        Binding = new ConnectorBindingState { ConnectorId = "solana-main", ExternalRef = "m",
            CapitalBasisUsd = capital, CapitalHistory = capHist },
    };

    [Fact]
    public void Trajectory_SubtractsAssetCapitalFromSavings_NoDoubleCount()
    {
        var now = M(2026, 3);
        var states = new List<StreamGrainState>
        {
            Income("Salary", (M(2026,1), 1000m)),
            Asset("SOL", 600m, new() { new CapitalPoint { At = M(2026,2), CapitalUsd = 600m } }, (M(2026,2), 600m)),
        };

        var pts = StreamAnalyticsService.ComputeNetWorthTrajectory(states, openingBalance: 0m, futureMonths: 0, now: now);

        var feb = pts.Single(p => p.Timestamp.Month == 2 && !p.IsProjected);
        feb.StableBalance.Should().Be(400m);   // income 1000 − capital 600
        feb.VolatileBalance.Should().Be(600m); // value
        feb.Balance.Should().Be(1000m);        // net worth
    }

    [Fact]
    public void Trajectory_AddsOpeningBalanceToSavings()
    {
        var now = M(2026, 2);
        var states = new List<StreamGrainState> { Income("Salary", (M(2026,1), 1000m)) };

        var pts = StreamAnalyticsService.ComputeNetWorthTrajectory(states, openingBalance: 5000m, futureMonths: 0, now: now);

        pts.First(p => p.Timestamp.Month == 1).StableBalance.Should().Be(6000m);
    }

    [Fact]
    public void Trajectory_Projection_HoldsAssetsFlat_AndWidensBand()
    {
        var now = M(2026, 4);
        var states = new List<StreamGrainState>
        {
            Income("Salary", (M(2026,1), 1000m), (M(2026,2), 1000m), (M(2026,3), 1000m)),
            Asset("SOL", 600m, new() { new CapitalPoint { At = M(2026,1), CapitalUsd = 600m } },
                (M(2026,1), 600m), (M(2026,2), 60m), (M(2026,3), -40m)), // value 600 → 660 → 620
        };

        var pts = StreamAnalyticsService.ComputeNetWorthTrajectory(states, openingBalance: 0m, futureMonths: 3, now: now);

        var projected = pts.Where(p => p.IsProjected).ToList();
        projected.Should().HaveCount(3);
        projected.Should().OnlyContain(p => p.VolatileBalance == 620m);
        projected[0].BandHigh!.Value.Should().BeGreaterThan(projected[0].Balance);
        projected[2].BandHigh!.Value.Should().BeGreaterThan(projected[0].BandHigh!.Value);
    }
}
