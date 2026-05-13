using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Domain;

namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class ConnectorBindingState
{
    [Id(0)] public string ConnectorId { get; set; } = "";
    [Id(1)] public string ExternalRef { get; set; } = "";
    [Id(2)] public DateTimeOffset? LastSync { get; set; }

    public static ConnectorBindingState From(ConnectorBinding binding) => new()
    {
        ConnectorId = binding.ConnectorId.Value,
        ExternalRef = binding.ExternalRef,
        LastSync = binding.LastSync,
    };

    public ConnectorBinding ToDomain() => ConnectorBinding.Create(
            new ConnectorId(ConnectorId),
            ExternalRef,
            LastSync)
        .Match(
            binding => binding,
            _ => throw new InvalidOperationException("Persisted ConnectorBinding state is invalid."));
}
