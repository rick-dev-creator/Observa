namespace Observa.Connectors.Abstractions;

public sealed record ConnectorConfigField(
    string Name,
    string DisplayName,
    ConnectorConfigFieldKind Kind,
    bool Required = true,
    string? Description = null);

public enum ConnectorConfigFieldKind
{
    Text,
    Secret,
    Url,
}
