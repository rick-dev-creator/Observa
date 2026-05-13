using Observa.Connectors.Abstractions;

namespace Observa.Features.Connectors.Registry;

public sealed class ConnectorRegistry(IEnumerable<IConnector> connectors) : IConnectorRegistry
{
    private readonly Dictionary<ConnectorId, IConnector> _byId =
        connectors.ToDictionary(c => c.Id);

    public IConnector? Find(ConnectorId id) =>
        _byId.TryGetValue(id, out var connector) ? connector : null;

    public IReadOnlyCollection<IConnector> All => _byId.Values;
}
