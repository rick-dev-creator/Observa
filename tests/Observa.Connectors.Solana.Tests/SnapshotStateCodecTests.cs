using FluentAssertions;
using Observa.Connectors.Solana;

namespace Observa.Connectors.Solana.Tests;

public sealed class SnapshotStateCodecTests
{
    [Fact]
    public void RoundTrips_QuantityAndPrice()
    {
        var json = SnapshotStateCodec.Serialize(12.5m, 140.25m);
        var parsed = SnapshotStateCodec.TryParse(json);
        parsed.Should().NotBeNull();
        parsed!.Value.Quantity.Should().Be(12.5m);
        parsed.Value.Price.Should().Be(140.25m);
    }

    [Fact]
    public void TryParse_NullOrGarbage_ReturnsNull()
    {
        SnapshotStateCodec.TryParse(null).Should().BeNull();
        SnapshotStateCodec.TryParse("not json").Should().BeNull();
    }
}
