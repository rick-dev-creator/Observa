using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Observa.Connectors.Patreon.Tests;

/// <summary>
/// Integration tests for <see cref="PatreonApiClient"/>.
/// Set <c>PATREON_ACCESS_TOKEN</c> and <c>PATREON_CAMPAIGN_ID</c> environment variables to run.
/// When env vars are missing, tests that need them log and return (pass trivially).
/// </summary>
public sealed class PatreonApiClientTests
{
    private readonly ITestOutputHelper _output;

    public PatreonApiClientTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task FetchPledgesAsync_EmptyCampaignId_ReturnsEmpty()
    {
        var client = BuildClient();

        var events = await client.FetchPledgesAsync(
            campaignId: "",
            accessToken: "anything",
            since: null,
            ct: CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchPledgesAsync_EmptyToken_ReturnsEmpty()
    {
        var client = BuildClient();

        var events = await client.FetchPledgesAsync(
            campaignId: "12345",
            accessToken: "",
            since: null,
            ct: CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchPledgesAsync_WithRealToken_ReturnsHistoricalEvents()
    {
        if (!TryGetCredentials(out var token, out var campaignId)) return;

        var client = BuildClient();

        var events = await client.FetchPledgesAsync(
            campaignId: campaignId,
            accessToken: token,
            since: null,
            ct: CancellationToken.None);

        _output.WriteLine($"Fetched {events.Count} events from Patreon campaign {campaignId}.");
        events.Should().NotBeNull();
        if (events.Count > 0)
        {
            var first = events.MinBy(e => e.OccurredAt)!;
            var last = events.MaxBy(e => e.OccurredAt)!;
            _output.WriteLine($"Range: {first.OccurredAt:yyyy-MM-dd} → {last.OccurredAt:yyyy-MM-dd}");
            events.Should().OnlyContain(e => e.AmountUsd > 0);
            events.Select(e => e.ExternalEventId).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public async Task FetchPledgesAsync_WithSinceFilter_ReturnsOnlyRecentEvents()
    {
        if (!TryGetCredentials(out var token, out var campaignId)) return;

        var since = DateTimeOffset.UtcNow.AddDays(-60);
        var client = BuildClient();

        var events = await client.FetchPledgesAsync(
            campaignId: campaignId,
            accessToken: token,
            since: since,
            ct: CancellationToken.None);

        _output.WriteLine($"Fetched {events.Count} events since {since:yyyy-MM-dd}.");
        events.Should().OnlyContain(e => e.OccurredAt >= since);
    }

    [Fact]
    public async Task FetchPledgesAsync_BackfillContainsOlderEventsThanWindowedFetch()
    {
        if (!TryGetCredentials(out var token, out var campaignId)) return;

        var client = BuildClient();

        var fullHistory = await client.FetchPledgesAsync(campaignId, token, since: null, ct: CancellationToken.None);
        var recentOnly = await client.FetchPledgesAsync(campaignId, token,
            since: DateTimeOffset.UtcNow.AddDays(-30), ct: CancellationToken.None);

        _output.WriteLine($"Full backfill: {fullHistory.Count}; recent: {recentOnly.Count}.");
        fullHistory.Count.Should().BeGreaterThanOrEqualTo(recentOnly.Count);
    }

    private bool TryGetCredentials(out string token, out string campaignId)
    {
        token = Environment.GetEnvironmentVariable("PATREON_ACCESS_TOKEN") ?? "";
        campaignId = Environment.GetEnvironmentVariable("PATREON_CAMPAIGN_ID") ?? "";

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(campaignId))
        {
            _output.WriteLine("PATREON_ACCESS_TOKEN / PATREON_CAMPAIGN_ID not set; test skipped.");
            return false;
        }
        return true;
    }

    private static PatreonApiClient BuildClient()
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri("https://www.patreon.com/api/oauth2/v2/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        return new PatreonApiClient(http, NullLogger<PatreonApiClient>.Instance);
    }
}
