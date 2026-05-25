using FluentAssertions;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class CapitalHistoryTests
{
    private static DateTimeOffset T(int day) => new(2026, 5, day, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CapitalAt_UsesLatestPointAtOrBeforeInstant()
    {
        var b = new ConnectorBindingState
        {
            ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = 300m,
            CapitalHistory = new()
            {
                new CapitalPoint { At = T(1), CapitalUsd = 100m },
                new CapitalPoint { At = T(5), CapitalUsd = 300m },
            },
        };

        StreamAnalyticsService.CapitalAt(b, T(3)).Should().Be(100m);
        StreamAnalyticsService.CapitalAt(b, T(9)).Should().Be(300m);
    }

    [Fact]
    public void CapitalAt_BeforeFirstPoint_IsZero()
    {
        var b = new ConnectorBindingState
        {
            ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = 100m,
            CapitalHistory = new() { new CapitalPoint { At = T(5), CapitalUsd = 100m } },
        };

        StreamAnalyticsService.CapitalAt(b, T(1)).Should().Be(0m);
    }

    [Fact]
    public void CapitalAt_NoHistory_FallsBackToCurrentCapital()
    {
        var b = new ConnectorBindingState
        {
            ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = 250m,
            CapitalHistory = new(),
        };

        StreamAnalyticsService.CapitalAt(b, T(3)).Should().Be(250m);
    }
}
