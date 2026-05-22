using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Crucible.Domain.Errors;
using Crucible.Domain.Results;
using Observa.Features.Streams.Errors;

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
                DomainErrors.Money.NegativeAmount,
                "Money amount must be non-negative.",
                nameof(Amount)));
        return Result.Success();
    }

    /// <summary>
    /// Creates a Money that may be negative or zero. Used for Performance flow events
    /// (gains/losses). The non-negative invariant of <see cref="Create"/> is intentionally
    /// NOT applied here — sign validity is enforced by the Stream aggregate per direction.
    /// </summary>
    public static Result<Money> CreateSigned(decimal amount) =>
        Result<Money>.Success(new Money { Amount = amount });

    public static Money Zero => Create(0m).Match(
        money => money,
        errors => throw new ValueObjectException(
            $"Money.Zero construction failed: {string.Join(",", errors.Select(e => e.ErrorCode))}"));
}
