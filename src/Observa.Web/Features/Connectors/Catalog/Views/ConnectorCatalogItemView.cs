namespace Observa.Features.Connectors.Catalog.Views;

public sealed record ConnectorCatalogItemView(
    string Id,
    string DisplayName,
    string Description,
    TimeSpan PollInterval,
    IReadOnlyList<ConnectorFieldView> ConfigFields);
