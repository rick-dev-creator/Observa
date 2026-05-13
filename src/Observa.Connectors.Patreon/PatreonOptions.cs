namespace Observa.Connectors.Patreon;

public sealed class PatreonOptions
{
    public const string SectionName = "Connectors:Patreon";

    public string ApiBaseUrl { get; set; } = "https://www.patreon.com/api/oauth2/v2/";
    public string? AccessToken { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(6);
}
