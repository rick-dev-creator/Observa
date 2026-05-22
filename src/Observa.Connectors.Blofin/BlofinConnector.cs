using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Blofin;

public sealed class BlofinConnector : IConnector
{
    private readonly BlofinOptions _options;
    private readonly BlofinAffiliateClient _api;

    public BlofinConnector(BlofinOptions options, BlofinAffiliateClient api)
    {
        if (string.IsNullOrWhiteSpace(options.Id))
            throw new InvalidOperationException("BlofinOptions.Id is required.");

        _options = options;
        _api = api;

        Id = new ConnectorId(options.Id);
        Metadata = new ConnectorMetadata(
            DisplayName: string.IsNullOrWhiteSpace(options.DisplayName)
                ? $"BloFin ({options.Id})"
                : $"BloFin — {options.DisplayName}",
            Description: "Pulls daily affiliate commission from BloFin and records it as income flow events " +
                         "(one event per day, total commission across all invitees).",
            PollInterval: options.PollInterval,
            ConfigSchema:
            [
                // Credentials live in app configuration. The per-stream external
                // reference is just a free-text label (e.g. the referral code);
                // the connector aggregates all invitees regardless of its value.
                new ConnectorConfigField("Label", "Label", ConnectorConfigFieldKind.Text,
                    Description: "Any label for this binding (e.g. your referral code). Not used for filtering."),
            ]);
    }

    public ConnectorId Id { get; }

    public ConnectorMetadata Metadata { get; }

    public Task<IReadOnlyList<ConnectorFlowEvent>> FetchEventsAsync(
        ConnectorFetchContext context,
        CancellationToken ct) =>
        _api.FetchDailyRebatesAsync(
            apiKey: _options.ApiKey ?? "",
            secretKey: _options.SecretKey ?? "",
            passphrase: _options.Passphrase ?? "",
            since: context.Since,
            historyDays: _options.HistoryDays,
            ct);
}
