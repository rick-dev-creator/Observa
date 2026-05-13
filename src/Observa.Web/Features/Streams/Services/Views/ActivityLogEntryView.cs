namespace Observa.Features.Streams.Services.Views;

public sealed record ActivityLogEntryView(
    DateTimeOffset Timestamp,
    string Kind,
    string Message,
    IReadOnlyDictionary<string, string>? Details);
