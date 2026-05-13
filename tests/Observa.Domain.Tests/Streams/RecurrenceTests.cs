using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Domain.Tests.Streams;

public sealed class RecurrenceTests
{
    [Theory]
    [InlineData(Cadence.Monthly, 1)]
    [InlineData(Cadence.Monthly, 15)]
    [InlineData(Cadence.Monthly, 31)]
    public void Create_MonthlyWithValidAnchor_Succeeds(Cadence cadence, int anchor)
    {
        var result = Recurrence.Create(cadence, anchor, Variability.Fixed);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(-1)]
    public void Create_MonthlyWithInvalidAnchor_Fails(int anchor)
    {
        var result = Recurrence.Create(Cadence.Monthly, anchor, Variability.Fixed);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == "RECURRENCE_MONTHLY_ANCHOR_RANGE");
    }

    [Theory]
    [InlineData(Cadence.Weekly, 1)]
    [InlineData(Cadence.Weekly, 7)]
    [InlineData(Cadence.Biweekly, 3)]
    public void Create_WeeklyOrBiweeklyWithValidAnchor_Succeeds(Cadence cadence, int anchor)
    {
        var result = Recurrence.Create(cadence, anchor, Variability.Variable);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(Cadence.Weekly, 0)]
    [InlineData(Cadence.Weekly, 8)]
    [InlineData(Cadence.Biweekly, 0)]
    [InlineData(Cadence.Biweekly, 8)]
    public void Create_WeeklyOrBiweeklyWithInvalidAnchor_Fails(Cadence cadence, int anchor)
    {
        var result = Recurrence.Create(cadence, anchor, Variability.Fixed);

        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(Cadence.Irregular)]
    [InlineData(Cadence.OneOff)]
    public void Create_IrregularOrOneOff_AnyAnchorSucceeds(Cadence cadence)
    {
        var result = Recurrence.Create(cadence, 999, Variability.Variable);

        result.IsSuccess.Should().BeTrue();
    }
}
