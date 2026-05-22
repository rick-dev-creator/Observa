namespace Observa.Connectors.Blofin;

public sealed class BlofinOptions
{
    public const string SectionName = "Connectors:Blofin";

    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>BloFin affiliate API key (ACCESS-KEY header).</summary>
    public string? ApiKey { get; set; }

    /// <summary>BloFin affiliate API secret used to sign requests.</summary>
    public string? SecretKey { get; set; }

    /// <summary>BloFin affiliate API passphrase (ACCESS-PASSPHRASE header).</summary>
    public string? Passphrase { get; set; }

    /// <summary>Affiliate API is always production — never demo.</summary>
    public string ApiBaseUrl { get; set; } = "https://openapi.blofin.com";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Fallback look-back (days) for the first poll when invitee registration dates are
    /// unavailable. Normally the first poll backfills from the earliest invitee's registration,
    /// so this only applies if no invitees report a register time. Note: BloFin's daily endpoint
    /// only serves a rolling window (~last 4 months), so older history is not recoverable per day.
    /// </summary>
    public int HistoryDays { get; set; } = 365;
}
