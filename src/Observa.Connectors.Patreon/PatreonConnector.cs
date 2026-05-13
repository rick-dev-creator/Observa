using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Patreon;

public sealed class PatreonConnector : IConnector
{
    private readonly PatreonOptions _options;
    private readonly PatreonApiClient _api;

    public PatreonConnector(PatreonOptions options, PatreonApiClient api)
    {
        if (string.IsNullOrWhiteSpace(options.Id))
            throw new InvalidOperationException("PatreonOptions.Id is required.");

        _options = options;
        _api = api;

        Id = new ConnectorId(options.Id);
        Metadata = new ConnectorMetadata(
            DisplayName: string.IsNullOrWhiteSpace(options.DisplayName)
                ? $"Patreon ({options.Id})"
                : $"Patreon — {options.DisplayName}",
            Description: "Pulls member pledges from a Patreon campaign as income flow events.",
            PollInterval: options.PollInterval,
            ConfigSchema:
            [
                new ConnectorConfigField("CampaignId", "Campaign ID", ConnectorConfigFieldKind.Text,
                    Description: "Numeric Patreon campaign identifier."),
            ]);
    }

    public ConnectorId Id { get; }

    public ConnectorMetadata Metadata { get; }

    public Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(
        ConnectorFetchContext context,
        CancellationToken ct) =>
        _api.FetchPledgesAsync(context.ExternalRef, _options.AccessToken ?? "", context.Since, ct);
}
