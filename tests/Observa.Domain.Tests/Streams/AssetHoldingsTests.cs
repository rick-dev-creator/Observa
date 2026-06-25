using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class AssetHoldingsTests
{
    [Fact]
    public void BuildAssetHolding_ComputesReturnFromValueMinusCapital()
    {
        var state = new StreamGrainState
        {
            Id = Guid.NewGuid(), Name = "SOL", Category = "Crypto", Direction = Direction.Performance,
            Status = StreamStatus.Active,
            Events = new()
            {
                new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow.AddDays(-2),
                    Amount = new MoneyState { Amount = 200m }, Source = IngestionSource.Connector, ExternalRef = "a" },
                new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow.AddDays(-1),
                    Amount = new MoneyState { Amount = 50m }, Source = IngestionSource.Connector, ExternalRef = "b" },
            },
            Binding = new ConnectorBindingState { ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = 200m },
        };

        var h = StreamAnalyticsService.BuildAssetHolding(state)!;

        h.ValueUsd.Should().Be(250m);
        h.CapitalUsd.Should().Be(200m);
        h.ReturnUsd.Should().Be(50m);
        h.ReturnPct.Should().Be(0.25m);
    }

    [Fact]
    public void BuildAssetHolding_NonAsset_ReturnsNull()
    {
        var income = new StreamGrainState { Id = Guid.NewGuid(), Name = "Salary", Category = "Work",
            Direction = Direction.Income, Status = StreamStatus.Active };
        StreamAnalyticsService.BuildAssetHolding(income).Should().BeNull();
    }

    private static StreamGrainState Holding(decimal capital, params (DateTimeOffset At, decimal Amount)[] events) =>
        new()
        {
            Id = Guid.NewGuid(), Name = "SOL", Category = "Crypto", Direction = Direction.Performance,
            Status = StreamStatus.Active,
            Events = events.Select(e => new FlowEventSnapshot
            {
                Id = Guid.NewGuid(), OccurredAt = e.At, Amount = new MoneyState { Amount = e.Amount },
                Source = IngestionSource.Connector, ExternalRef = Guid.NewGuid().ToString(),
            }).ToList(),
            Binding = new ConnectorBindingState { ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = capital },
        };

    [Fact]
    public void BuildAssetHolding_DerivesWindowedChangeFromEventHistory()
    {
        var now = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        // baseline +1000 (3d ago), +50 (2h ago), −30 (30m ago) ⇒ value 1020
        var state = Holding(1000m,
            (now.AddDays(-3), 1000m),
            (now.AddHours(-2), 50m),
            (now.AddMinutes(-30), -30m));

        var h = StreamAnalyticsService.BuildAssetHolding(state, now)!;

        h.ValueUsd.Should().Be(1020m);
        h.ReturnUsd.Should().Be(20m);
        // 24h ago only the baseline had landed ⇒ value 1000; change = +20 (+2%).
        h.Change24hUsd.Should().Be(20m);
        h.Change24hPct.Should().Be(0.02m);
        // 7d ago nothing existed ⇒ value 0; change = full +1020, pct undefined (base 0).
        h.Change7dUsd.Should().Be(1020m);
        h.Change7dPct.Should().BeNull();
        h.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void BuildAssetHolding_Sparkline_StartsAtFirstEvent_EndsAtCurrentValue()
    {
        var now = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        var state = Holding(1000m, (now.AddDays(-2), 1000m), (now.AddHours(-1), 100m));

        var h = StreamAnalyticsService.BuildAssetHolding(state, now)!;

        h.Sparkline.Should().HaveCount(24);
        h.Sparkline[0].Should().Be(1000m);    // first sample at/after the baseline event
        h.Sparkline[^1].Should().Be(1100m);   // last sample = current value
    }

    [Fact]
    public void BuildAssetHolding_FlagsExitedPositionAsClosed()
    {
        var now = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        // bought 500, then moved it all out ⇒ value 0 but capital was real
        var state = Holding(500m, (now.AddDays(-2), 500m), (now.AddHours(-1), -500m));

        var h = StreamAnalyticsService.BuildAssetHolding(state, now)!;

        h.ValueUsd.Should().Be(0m);
        h.IsClosed.Should().BeTrue();
    }
}
