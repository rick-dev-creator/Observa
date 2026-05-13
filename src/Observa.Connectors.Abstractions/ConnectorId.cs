namespace Observa.Connectors.Abstractions;

public readonly record struct ConnectorId(string Value)
{
    public override string ToString() => Value;
}
