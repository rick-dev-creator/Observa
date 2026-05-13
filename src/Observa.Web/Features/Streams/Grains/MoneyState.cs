using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class MoneyState
{
    [Id(0)] public decimal Amount { get; set; }

    public static MoneyState From(Money money) => new() { Amount = money.Amount };

    public Money ToDomain() => Money.Create(Amount).Match(
        money => money,
        _ => throw new InvalidOperationException($"Persisted Money state has invalid amount {Amount}."));
}
