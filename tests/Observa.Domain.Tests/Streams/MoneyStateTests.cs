using FluentAssertions;
using Observa.Features.Streams.Grains;

namespace Observa.Domain.Tests.Streams;

public sealed class MoneyStateTests
{
    [Fact]
    public void ToDomain_WithNegativeAmount_RehydratesFaithfully()
    {
        // Performance flow events persist signed amounts; reading state back must NOT
        // re-impose the non-negative rule (regression: used to throw via Money.Create).
        var state = new MoneyState { Amount = -1392.03m };

        var money = state.ToDomain();

        money.Amount.Should().Be(-1392.03m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2500.50)]
    public void ToDomain_WithNonNegativeAmount_RehydratesFaithfully(decimal amount)
    {
        new MoneyState { Amount = amount }.ToDomain().Amount.Should().Be(amount);
    }
}
