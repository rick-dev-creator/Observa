using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

public sealed record FlowEventView(
    Guid Id,
    DateTimeOffset OccurredAt,
    decimal Amount,
    IngestionSource Source,
    string? ExternalRef);
