using FluentAssertions;
using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Domain;
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
}
