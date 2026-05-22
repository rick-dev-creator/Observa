using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Observa.Connectors.Blofin.Tests;

/// <summary>
/// Tests for <see cref="BlofinAffiliateClient"/> and the signing scheme.
/// The live test runs only when BLOFIN_API_KEY / BLOFIN_SECRET_KEY / BLOFIN_PASSPHRASE
/// are set; otherwise it logs and passes trivially.
/// </summary>
public sealed class BlofinAffiliateClientTests
{
    private readonly ITestOutputHelper _output;

    public BlofinAffiliateClientTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task FetchDailyRebatesAsync_MissingCredentials_ReturnsEmpty()
    {
        var client = BuildClient();

        var events = await client.FetchDailyRebatesAsync(
            apiKey: "", secretKey: "", passphrase: "",
            since: null, historyDays: 365, ct: CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchDailyRebatesAsync_WindowAlreadyElapsed_ReturnsEmpty()
    {
        var client = BuildClient();

        // since is in the future relative to "now" → no window to fetch, short-circuits before any HTTP call.
        var events = await client.FetchDailyRebatesAsync(
            apiKey: "k", secretKey: "s", passphrase: "p",
            since: DateTimeOffset.UtcNow.AddDays(1), historyDays: 365, ct: CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public void CreateSignature_IsBase64OfLowercaseHexHmacSha256()
    {
        const string path = "/api/v1/affiliate/basic";
        const string method = "GET";
        const string timestamp = "1700000000000";
        const string nonce = "abc123";
        const string body = "";
        const string secret = "test-secret";

        var actual = BlofinCrypto.CreateSignature(path, method, timestamp, nonce, body, secret);

        // Independently recompute the documented scheme: base64( lowerhex( HMAC-SHA256(secret, prehash) ) ).
        var prehash = $"{path}{method}{timestamp}{nonce}{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash))).ToLowerInvariant();
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(expectedHex));

        actual.Should().Be(expected);
        Encoding.UTF8.GetString(Convert.FromBase64String(actual))
            .Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task FetchDailyRebatesAsync_WithRealCredentials_AggregatesPerDay()
    {
        var apiKey = Environment.GetEnvironmentVariable("BLOFIN_API_KEY") ?? "";
        var secret = Environment.GetEnvironmentVariable("BLOFIN_SECRET_KEY") ?? "";
        var passphrase = Environment.GetEnvironmentVariable("BLOFIN_PASSPHRASE") ?? "";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(passphrase))
        {
            _output.WriteLine("BLOFIN_API_KEY / BLOFIN_SECRET_KEY / BLOFIN_PASSPHRASE not set; test skipped.");
            return;
        }

        var client = BuildClient();
        var events = await client.FetchDailyRebatesAsync(
            apiKey, secret, passphrase, since: null, historyDays: 90, ct: CancellationToken.None);

        _output.WriteLine($"Fetched {events.Count} daily rebate events.");
        events.Select(e => e.ExternalEventId).Should().OnlyHaveUniqueItems();
        events.Should().OnlyContain(e => e.AmountUsd != 0m);
    }

    private static BlofinAffiliateClient BuildClient()
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri("https://openapi.blofin.com"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        return new BlofinAffiliateClient(http, NullLogger<BlofinAffiliateClient>.Instance);
    }
}
