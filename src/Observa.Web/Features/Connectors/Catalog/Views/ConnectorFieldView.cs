namespace Observa.Features.Connectors.Catalog.Views;

public sealed record ConnectorFieldView(
    string Name,
    string DisplayName,
    string Kind,
    bool Required,
    string? Description);
