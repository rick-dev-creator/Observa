using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Crucible.Domain.Errors;
using Crucible.Domain.Results;
using Observa.Features.Streams.Errors;

namespace Observa.Features.Streams.ValueObjects;

[ValueObject]
[GenerateSerializer]
public sealed partial record Money : ValueObject
{
    [Id(0)] public decimal Amount { get; init; }

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

    public static Money Zero => Create(0m).Match(
        money => money,
        errors => throw new ValueObjectException(
            $"Money.Zero construction failed: {string.Join(",", errors.Select(e => e.ErrorCode))}"));
}
