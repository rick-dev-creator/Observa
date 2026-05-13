namespace Observa.Connectors.Patreon;

public sealed class PatreonOptions
{
    public const string SectionName = "Connectors:Patreon";

    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AccessToken { get; set; }
    public string ApiBaseUrl { get; set; } = "https://www.patreon.com/api/oauth2/v2/";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(6);
}
