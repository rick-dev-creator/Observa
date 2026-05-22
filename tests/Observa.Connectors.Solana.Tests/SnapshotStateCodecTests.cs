using FluentAssertions;
using Observa.Connectors.Solana;

namespace Observa.Connectors.Solana.Tests;

public sealed class SnapshotStateCodecTests
{
    [Fact]
    public void RoundTrips_QuantityPriceAndCapital()
    {
        var json = SnapshotStateCodec.Serialize(12.5m, 140.25m, 1000m);
        var parsed = SnapshotStateCodec.TryParse(json);
        parsed.Should().NotBeNull();
        parsed!.Value.Should().Be((12.5m, 140.25m, 1000m));
    }

    [Fact]
    public void TryParse_NullOrGarbage_ReturnsNull()
    {
        SnapshotStateCodec.TryParse(null).Should().BeNull();
        SnapshotStateCodec.TryParse("not json").Should().BeNull();
    }
}
