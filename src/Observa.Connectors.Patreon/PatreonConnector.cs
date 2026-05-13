using Microsoft.Extensions.Options;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Patreon;

public sealed class PatreonConnector(PatreonApiClient api, IOptions<PatreonOptions> options) : IConnector
{
    public static readonly ConnectorId ConnectorId = new("patreon");

    public ConnectorId Id => ConnectorId;

    public ConnectorMetadata Metadata { get; } = new(
        DisplayName: "Patreon",
        Description: "Pulls member pledges from a Patreon campaign as income flow events.",
        PollInterval: options.Value.PollInterval,
        ConfigSchema:
        [
            new ConnectorConfigField("CampaignId", "Campaign ID", ConnectorConfigFieldKind.Text,
                Description: "Numeric Patreon campaign identifier."),
            new ConnectorConfigField("AccessToken", "Creator Access Token", ConnectorConfigFieldKind.Secret,
                Description: "OAuth2 access token with campaigns.members scope."),
        ]);

    public Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(
        string externalRef,
        DateTimeOffset since,
        CancellationToken ct) => api.FetchPledgesAsync(externalRef, since, ct);
}
