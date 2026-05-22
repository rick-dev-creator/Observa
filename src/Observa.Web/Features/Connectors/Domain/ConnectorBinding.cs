using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Crucible.Domain.Errors;
using Crucible.Domain.Results;
using Observa.Connectors.Abstractions;
using Observa.Features.Streams.Errors;

namespace Observa.Features.Connectors.Domain;

[ValueObject]
public sealed partial record ConnectorBinding : ValueObject
{
    public ConnectorId ConnectorId { get; init; }
    public string ExternalRef { get; init; } = "";
    public DateTimeOffset? LastSync { get; init; }
    public string? SnapshotState { get; init; }

    private ConnectorBinding() { }

    private static partial Result __ValidateConstruction(ConnectorId connectorId, string externalRef, DateTimeOffset? lastSync, string? snapshotState)
    {
        var errors = new List<IError>();
        if (string.IsNullOrWhiteSpace(connectorId.Value))
            errors.Add(new ValidationError(
                DomainErrors.ConnectorBinding.ConnectorIdRequired,
                "ConnectorId is required.",
                nameof(ConnectorId)));
        if (string.IsNullOrWhiteSpace(externalRef))
            errors.Add(new ValidationError(
                DomainErrors.ConnectorBinding.ExternalRefRequired,
                "External reference is required.",
                nameof(ExternalRef)));
        return errors.Count > 0 ? Result.Failure(errors) : Result.Success();
    }
}
