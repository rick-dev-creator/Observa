using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Blofin;

/// <summary>
/// Talks to the BloFin affiliate API and turns daily affiliate commission into
/// flow events. Each event is one calendar day: the total commission generated
/// across every invitee, summed into a single USD amount.
/// </summary>
public sealed class BlofinAffiliateClient(HttpClient http, ILogger<BlofinAffiliateClient> logger)
{
    // The daily-info endpoint caps each call to a bounded window; slice longer
    // backfills into chunks well under that cap.
    private static readonly TimeSpan WindowSize = TimeSpan.FromDays(90);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Fetches affiliate commission from <paramref name="since"/> (or <paramref name="historyDays"/>
    /// back on the first run) to now, aggregated to one flow event per day.
    /// </summary>
    public async Task<IReadOnlyList<ConnectorFlowEvent>> FetchDailyRebatesAsync(
        string apiKey,
        string secretKey,
        string passphrase,
        DateTimeOffset? since,
        int historyDays,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(secretKey) ||
            string.IsNullOrWhiteSpace(passphrase))
        {
            logger.LogWarning("BloFin fetch skipped: API credentials are not fully configured.");
            return [];
        }

        var to = DateTimeOffset.UtcNow;

        var (invitees, earliestRegistration) =
            await GetAllInviteesAsync(apiKey, secretKey, passphrase, ct);
        if (invitees.Count == 0)
        {
            logger.LogInformation("BloFin affiliate: no invitees returned; nothing to ingest.");
            return [];
        }

        // First poll (no prior sync): reach back to the earliest invitee's registration so the
        // backfill captures everything the API can serve. Patreon does the equivalent from each
        // patron's pledge-start date. Fall back to HistoryDays only if no registration dates exist.
        var from = since
            ?? earliestRegistration
            ?? to.AddDays(-Math.Max(historyDays, 1));
        if (from >= to) return [];

        // Sum commission per UTC day across all invitees.
        var byDay = new Dictionary<DateOnly, decimal>();

        foreach (var uid in invitees)
        {
            ct.ThrowIfCancellationRequested();
            await foreach (var record in GetDailyInfoAsync(apiKey, secretKey, passphrase, uid, from, to, ct))
            {
                if (!TryParseTimestamp(record.CommissionTime, out var when)) continue;
                var commission = ParseDecimal(record.Commission);
                if (commission == 0m) continue;

                var day = DateOnly.FromDateTime(when.UtcDateTime);
                byDay[day] = byDay.GetValueOrDefault(day) + commission;
            }
        }

        var events = byDay
            .Where(kv => kv.Value != 0m)
            .OrderBy(kv => kv.Key)
            .Select(kv => new ConnectorFlowEvent(
                ExternalEventId: $"blofin-{kv.Key:yyyyMMdd}",
                OccurredAt: new DateTimeOffset(kv.Key.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                AmountUsd: Math.Round(kv.Value, 2)))
            .ToList();

        logger.LogInformation(
            "BloFin affiliate: {Invitees} invitees, synthesized {Events} daily flow events (since {Since}).",
            invitees.Count, events.Count, from.ToString("O"));

        return events;
    }

    private async Task<(List<string> Uids, DateTimeOffset? EarliestRegistration)> GetAllInviteesAsync(
        string apiKey, string secretKey, string passphrase, CancellationToken ct)
    {
        var uids = new List<string>();
        DateTimeOffset? earliest = null;
        string? cursor = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var path = "/api/v1/affiliate/invitees?limit=100";
            if (cursor is not null) path += $"&after={cursor}";

            var resp = await SendSignedGetAsync<List<InviteeDto>>(
                path, apiKey, secretKey, passphrase, ct);
            var data = resp?.Data;
            if (data is null || data.Count == 0) break;

            foreach (var d in data)
            {
                if (string.IsNullOrWhiteSpace(d.Uid)) continue;
                uids.Add(d.Uid);
                if (TryParseTimestamp(d.RegisterTime, out var registered) &&
                    (earliest is null || registered < earliest))
                    earliest = registered;
            }

            if (data.Count < 100) break;
            cursor = data[^1].Id.ToString(CultureInfo.InvariantCulture);
            await Task.Delay(100, ct);
        }

        return (uids, earliest);
    }

    private async IAsyncEnumerable<DailyCommissionDto> GetDailyInfoAsync(
        string apiKey, string secretKey, string passphrase, string uid,
        DateTimeOffset from, DateTimeOffset to,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var cursor = from;
        while (cursor < to)
        {
            ct.ThrowIfCancellationRequested();
            var windowEnd = cursor + WindowSize;
            if (windowEnd > to) windowEnd = to;

            var begin = cursor.ToUnixTimeMilliseconds();
            var end = windowEnd.ToUnixTimeMilliseconds();
            var path = $"/api/v1/affiliate/invitees/daily/info?uid={Uri.EscapeDataString(uid)}" +
                       $"&begin={begin}&end={end}&limit=100";

            var resp = await SendSignedGetAsync<List<DailyCommissionDto>>(
                path, apiKey, secretKey, passphrase, ct);
            foreach (var record in resp?.Data ?? [])
                yield return record;

            cursor = windowEnd;
            await Task.Delay(100, ct);
        }
    }

    private async Task<BlofinApiResponse<T>?> SendSignedGetAsync<T>(
        string path, string apiKey, string secretKey, string passphrase, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var sign = BlofinCrypto.CreateSignature(path, "GET", timestamp, nonce, "", secretKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("ACCESS-KEY", apiKey);
        request.Headers.Add("ACCESS-SIGN", sign);
        request.Headers.Add("ACCESS-TIMESTAMP", timestamp);
        request.Headers.Add("ACCESS-NONCE", nonce);
        request.Headers.Add("ACCESS-PASSPHRASE", passphrase);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("BloFin API {Path} returned {Status}: {Body}", path, (int)response.StatusCode, body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<BlofinApiResponse<T>>(stream, JsonOptions, ct);
    }

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static bool TryParseTimestamp(string? value, out DateTimeOffset when)
    {
        if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ms))
        {
            when = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            return true;
        }
        when = default;
        return false;
    }
}
