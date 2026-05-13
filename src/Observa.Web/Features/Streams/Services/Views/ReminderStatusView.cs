namespace Observa.Features.Streams.Services.Views;

public sealed record ReminderStatusView(
    string ConnectorId,
    string ConnectorDisplayName,
    TimeSpan PollInterval,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset? NextFireEstimate,
    DateTimeOffset? LastSyncAt);
