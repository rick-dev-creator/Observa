using Observa.Features.Connectors.Catalog.Views;
using Observa.Features.Connectors.Registry;

namespace Observa.Features.Connectors.Catalog;

public sealed class ConnectorCatalogService(IConnectorRegistry registry)
{
    public IReadOnlyList<ConnectorCatalogItemView> List() =>
        registry.All
            .OrderBy(c => c.Metadata.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(c => new ConnectorCatalogItemView(
                Id: c.Id.Value,
                DisplayName: c.Metadata.DisplayName,
                Description: c.Metadata.Description,
                PollInterval: c.Metadata.PollInterval,
                ConfigFields: c.Metadata.ConfigSchema
                    .Select(f => new ConnectorFieldView(
                        f.Name,
                        f.DisplayName,
                        f.Kind.ToString(),
                        f.Required,
                        f.Description))
                    .ToList()))
            .ToList();
}
