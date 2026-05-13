using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Crucible.Domain.Errors;
using Crucible.Domain.Results;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Errors;

namespace Observa.Features.Streams.ValueObjects;

[ValueObject]
[GenerateSerializer]
public sealed partial record Recurrence : ValueObject
{
    [Id(0)] public Cadence Cadence { get; init; }
    [Id(1)] public int Anchor { get; init; }
    [Id(2)] public Variability Variability { get; init; }

    private Recurrence() { }

    private static partial Result __ValidateConstruction(Cadence cadence, int anchor, Variability variability)
    {
        var errors = new List<IError>();

        switch (cadence)
        {
            case Cadence.Monthly when anchor is < 1 or > 31:
                errors.Add(new ValidationError(
                    DomainErrors.Recurrence.MonthlyAnchorRange,
                    "Monthly anchor must be between 1 and 31 (day of month).",
                    nameof(Anchor)));
                break;
            case Cadence.Weekly when anchor is < 1 or > 7:
                errors.Add(new ValidationError(
                    DomainErrors.Recurrence.WeeklyAnchorRange,
                    "Weekly anchor must be between 1 and 7 (ISO day of week).",
                    nameof(Anchor)));
                break;
            case Cadence.Biweekly when anchor is < 1 or > 7:
                errors.Add(new ValidationError(
                    DomainErrors.Recurrence.BiweeklyAnchorRange,
                    "Biweekly anchor must be between 1 and 7 (ISO day of week).",
                    nameof(Anchor)));
                break;
        }

        return errors.Count > 0 ? Result.Failure(errors) : Result.Success();
    }
}
