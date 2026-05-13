using Microsoft.Extensions.Options;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Patreon;

public sealed class PatreonApiClient(HttpClient http, IOptions<PatreonOptions> options)
{
    private readonly PatreonOptions _options = options.Value;

    public Task<IReadOnlyList<ConnectorFlowEvent>> FetchPledgesAsync(
        string campaignId,
        DateTimeOffset since,
        CancellationToken ct)
    {
        // TODO: implement Patreon API v2 call once OAuth + creator access is set up
        // GET /campaigns/{campaignId}/members?filter[since]=... → map to ConnectorFlowEvent
        // Configured base URL: _options.ApiBaseUrl; HttpClient already pre-configured.
        _ = http;
        _ = _options;
        _ = campaignId;
        _ = since;
        _ = ct;
        return Task.FromResult<IReadOnlyList<ConnectorFlowEvent>>(Array.Empty<ConnectorFlowEvent>());
    }
}
