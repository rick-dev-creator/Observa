using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

public sealed record StreamOperationsView(
    Guid Id,
    string Name,
    string Category,
    Direction Direction,
    StreamStatus Status,
    string? ConnectorId,
    string? ConnectorDisplayName,
    DateTimeOffset? LastConnectorPollAt,
    DateTimeOffset? NextPollEstimate,
    bool LastPollFailed,
    DateTimeOffset? LastEventAt,
    decimal? LastEventAmount,
    decimal? ExpectedAmount);
