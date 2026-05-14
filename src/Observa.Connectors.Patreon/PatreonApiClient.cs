using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Patreon;

public sealed class PatreonApiClient(HttpClient http, ILogger<PatreonApiClient> logger)
{
    private const string DefaultBaseUrl = "https://www.patreon.com/api/oauth2/v2/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<ConnectorFlowEvent>> FetchPledgesAsync(
        string campaignId,
        string accessToken,
        DateTimeOffset? since,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            logger.LogWarning("Patreon fetch skipped: campaignId is empty.");
            return Array.Empty<ConnectorFlowEvent>();
        }
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Patreon fetch skipped: access token is not configured.");
            return Array.Empty<ConnectorFlowEvent>();
        }

        if (http.BaseAddress is null)
            http.BaseAddress = new Uri(DefaultBaseUrl);

        var events = new List<ConnectorFlowEvent>();
        var memberCount = 0;

        var nextUrl = $"campaigns/{Uri.EscapeDataString(campaignId)}/members" +
                      "?fields%5Bmember%5D=" +
                      "patron_status,last_charge_date,last_charge_status," +
                      "lifetime_support_cents,currently_entitled_amount_cents," +
                      "pledge_relationship_start,pledge_cadence,will_pay_amount_cents" +
                      "&page%5Bcount%5D=200";

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                logger.LogError("Patreon API returned {Status}: {Body}", (int)res.StatusCode, body);
                res.EnsureSuccessStatusCode();
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            var page = await JsonSerializer.DeserializeAsync<MembersPage>(stream, JsonOptions, ct);
            if (page?.Data is null) break;

            foreach (var member in page.Data)
            {
                memberCount++;
                if (member.Attributes is null || string.IsNullOrEmpty(member.Id)) continue;
                ReconstructCharges(member.Id, member.Attributes, since, events);
            }

            nextUrl = page.Links?.Next;
        }

        logger.LogInformation(
            "Patreon campaign {CampaignId}: read {Members} members, synthesized {Events} flow events (since {Since}).",
            campaignId, memberCount, events.Count, since?.ToString("O") ?? "(all)");

        return events;
    }

    private static void ReconstructCharges(
        string memberId,
        MemberAttributes attrs,
        DateTimeOffset? since,
        List<ConnectorFlowEvent> sink)
    {
        if (attrs.PledgeRelationshipStart is not { } start) return;

        var amountCents = attrs.WillPayAmountCents
                          ?? attrs.CurrentlyEntitledAmountCents
                          ?? 0;
        if (amountCents <= 0) return;

        // Active patrons charge through today. Lapsed patrons stop at the last charge.
        var isActive = string.Equals(attrs.PatronStatus, "active_patron", StringComparison.OrdinalIgnoreCase);
        var end = isActive ? DateTimeOffset.UtcNow : attrs.LastChargeDate ?? start;
        if (end < start) return;

        var billingDay = start.Day;
        var cursor = new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero);
        while (cursor <= end)
        {
            var daysInMonth = DateTime.DaysInMonth(cursor.Year, cursor.Month);
            var chargeDate = new DateTimeOffset(
                cursor.Year, cursor.Month, Math.Min(billingDay, daysInMonth),
                0, 0, 0, TimeSpan.Zero);

            // Skip the partial first month if the relationship started after the billing day.
            if (chargeDate < start) { cursor = cursor.AddMonths(1); continue; }
            if (chargeDate > end) break;
            if (since is { } s && chargeDate < s) { cursor = cursor.AddMonths(1); continue; }

            sink.Add(new ConnectorFlowEvent(
                ExternalEventId: $"{memberId}-{chargeDate:yyyyMM}",
                OccurredAt: chargeDate,
                AmountUsd: amountCents / 100m));

            cursor = cursor.AddMonths(1);
        }
    }

    private sealed class MembersPage
    {
        public List<MemberResource>? Data { get; set; }
        public PageLinks? Links { get; set; }
    }

    private sealed class MemberResource
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public MemberAttributes? Attributes { get; set; }
    }

    private sealed class MemberAttributes
    {
        public DateTimeOffset? PledgeRelationshipStart { get; set; }
        public DateTimeOffset? LastChargeDate { get; set; }
        public string? LastChargeStatus { get; set; }
        public string? PatronStatus { get; set; }
        public int? CurrentlyEntitledAmountCents { get; set; }
        public int? WillPayAmountCents { get; set; }
        public long? LifetimeSupportCents { get; set; }
        public int? PledgeCadence { get; set; }
    }

    private sealed class PageLinks
    {
        [JsonPropertyName("next")]
        public string? Next { get; set; }
    }
}
