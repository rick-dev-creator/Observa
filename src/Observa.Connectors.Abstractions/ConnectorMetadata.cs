namespace Observa.Connectors.Abstractions;

public sealed record ConnectorMetadata(
    string DisplayName,
    string Description,
    TimeSpan PollInterval,
    IReadOnlyList<ConnectorConfigField> ConfigSchema);
