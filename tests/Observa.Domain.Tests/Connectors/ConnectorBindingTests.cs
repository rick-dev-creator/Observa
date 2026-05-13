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
        var result = ConnectorBinding.Create(new ConnectorId("patreon"), "campaign-123", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.ConnectorId.Value.Should().Be("patreon");
        result.Value.ExternalRef.Should().Be("campaign-123");
        result.Value.LastSync.Should().BeNull();
    }

    [Fact]
    public void Create_WithEmptyConnectorId_Fails()
    {
        var result = ConnectorBinding.Create(new ConnectorId(""), "campaign-123", null);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.ConnectorBinding.ConnectorIdRequired);
    }

    [Fact]
    public void Create_WithEmptyExternalRef_Fails()
    {
        var result = ConnectorBinding.Create(new ConnectorId("patreon"), "", null);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.ConnectorBinding.ExternalRefRequired);
    }

    [Fact]
    public void Create_WithLastSync_PreservesIt()
    {
        var ts = DateTimeOffset.Parse("2026-05-13T12:00:00Z");
        var result = ConnectorBinding.Create(new ConnectorId("patreon"), "campaign-123", ts);

        result.IsSuccess.Should().BeTrue();
        result.Value.LastSync.Should().Be(ts);
    }
}
