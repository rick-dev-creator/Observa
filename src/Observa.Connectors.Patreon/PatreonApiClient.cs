using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Patreon;

public sealed class PatreonApiClient(HttpClient http)
{
    public Task<IReadOnlyList<ConnectorFlowEvent>> FetchPledgesAsync(
        string campaignId,
        string accessToken,
        DateTimeOffset since,
        CancellationToken ct)
    {
        // TODO: implement Patreon API v2 call once OAuth + creator access is set up
        // GET /campaigns/{campaignId}/members?include=pledge_history&filter[since]=...
        // Authorization: Bearer {accessToken}
        _ = http;
        _ = campaignId;
        _ = accessToken;
        _ = since;
        _ = ct;
        return Task.FromResult<IReadOnlyList<ConnectorFlowEvent>>(Array.Empty<ConnectorFlowEvent>());
    }
}
