using FluentAssertions;
using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Domain;
using Observa.Features.Streams.Errors;

namespace Observa.Domain.Tests.Connectors;

public sealed class ConnectorBindingTests
{
    [Fact]
    public void Create_WithValidValues_Succeeds()
    {
        var result = ConnectorBinding.Create(new ConnectorId("patreon"), "campaign-123", null, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.ConnectorId.Value.Should().Be("patreon");
        result.Value.ExternalRef.Should().Be("campaign-123");
        result.Value.LastSync.Should().BeNull();
    }

    [Fact]
    public void Create_WithEmptyConnectorId_Fails()
    {
        var result = ConnectorBinding.Create(new ConnectorId(""), "campaign-123", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.ConnectorBinding.ConnectorIdRequired);
    }

    [Fact]
    public void Create_WithEmptyExternalRef_Fails()
    {
        var result = ConnectorBinding.Create(new ConnectorId("patreon"), "", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.ConnectorBinding.ExternalRefRequired);
    }

    [Fact]
    public void Create_WithLastSync_PreservesIt()
    {
        var ts = DateTimeOffset.Parse("2026-05-13T12:00:00Z");
        var result = ConnectorBinding.Create(new ConnectorId("patreon"), "campaign-123", ts, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.LastSync.Should().Be(ts);
    }

    [Fact]
    public void Create_DefaultsSnapshotStateToNull()
    {
        var binding = ConnectorBinding.Create(new ConnectorId("solana"), "mint123", null, null, null).Value;
        binding.SnapshotState.Should().BeNull();
    }

    [Fact]
    public void WithSnapshotState_SetsValueAndPreservesEquality()
    {
        var binding = ConnectorBinding.Create(new ConnectorId("solana"), "mint123", null, null, null).Value;
        var withState = binding with { SnapshotState = "{\"q\":1,\"p\":2}" };
        withState.SnapshotState.Should().Be("{\"q\":1,\"p\":2}");
        withState.ExternalRef.Should().Be("mint123");
    }

    [Fact]
    public void Create_CarriesCapitalBasis()
    {
        var b = ConnectorBinding.Create(new ConnectorId("solana"), "mint", null, null, 1234.50m).Value;
        b.CapitalBasisUsd.Should().Be(1234.50m);
        b.SnapshotState.Should().BeNull();
    }
}
