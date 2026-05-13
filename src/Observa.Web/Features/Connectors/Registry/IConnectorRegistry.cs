using Observa.Connectors.Abstractions;

namespace Observa.Features.Connectors.Registry;

public interface IConnectorRegistry
{
    IConnector? Find(ConnectorId id);
    IReadOnlyCollection<IConnector> All { get; }
}
