using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Crucible.Domain.Errors;
using Crucible.Domain.Results;

namespace Observa.Features.Streams.ValueObjects;

[ValueObject]
public sealed partial record Money : ValueObject
{
    public decimal Amount { get; init; }

    private Money() { }

    private static partial Result __ValidateConstruction(decimal amount)
    {
        if (amount < 0)
            return Result.Failure(new ValidationError(
                "MONEY_NEGATIVE_AMOUNT",
                "Money amount must be non-negative.",
                nameof(Amount)));
        return Result.Success();
    }

    public static Money Zero => Create(0m).Match(
        money => money,
        errors => throw new ValueObjectException(
            $"Money.Zero construction failed: {string.Join(",", errors.Select(e => e.ErrorCode))}"));
}
