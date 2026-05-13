using Crucible.Domain.Errors;
using FluentAssertions;
using Observa.Features.Streams.ValueObjects;
using Observa.Features.Streams.Errors;

namespace Observa.Domain.Tests.Streams;

public sealed class MoneyTests
{
    [Fact]
    public void Create_WithPositiveAmount_Succeeds()
    {
        var result = Money.Create(100m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(100m);
    }

    [Fact]
    public void Create_WithZero_Succeeds()
    {
        var result = Money.Create(0m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(0m);
    }

    [Fact]
    public void Create_WithNegativeAmount_FailsWithValidationError()
    {
        var result = Money.Create(-1m);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Money.NegativeAmount);
        result.Errors[0].Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void Zero_ReturnsAmountZero()
    {
        Money.Zero.Amount.Should().Be(0m);
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = Money.Create(50m).Value;
        var b = Money.Create(50m).Value;

        a.Should().Be(b);
    }
}
