using Observa.Features.Streams.Enums;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class RecurrenceState
{
    [Id(0)] public Cadence Cadence { get; set; }
    [Id(1)] public int Anchor { get; set; }
    [Id(2)] public Variability Variability { get; set; }

    public static RecurrenceState From(Recurrence recurrence) => new()
    {
        Cadence = recurrence.Cadence,
        Anchor = recurrence.Anchor,
        Variability = recurrence.Variability,
    };

    public Recurrence ToDomain() => Recurrence.Create(Cadence, Anchor, Variability).Match(
        recurrence => recurrence,
        _ => throw new InvalidOperationException("Persisted Recurrence state is invalid."));
}
