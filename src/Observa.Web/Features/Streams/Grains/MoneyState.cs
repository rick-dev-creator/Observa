using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class MoneyState
{
    [Id(0)] public decimal Amount { get; set; }

    public static MoneyState From(Money money) => new() { Amount = money.Amount };

    // Reconstruct faithfully with CreateSigned: persisted amounts were already validated by the
    // aggregate at ingest time (Performance allows negatives), so rehydration must not re-impose
    // the non-negative rule. Income/Outcome amounts are ≥0 regardless, so this stays correct for them.
    public Money ToDomain() => Money.CreateSigned(Amount).Match(
        money => money,
        _ => throw new InvalidOperationException($"Persisted Money state has invalid amount {Amount}."));
}
