using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

public sealed record StreamActivityView(
    Guid Id,
    string Name,
    string Category,
    Direction Direction,
    StreamStatus Status,
    decimal? ExpectedAmount,
    string? ScheduleSummary,
    string? ConnectorDisplayName,
    string? ConnectorExternalRef,
    ReminderStatusView? ReminderStatus,
    IReadOnlyList<ActivityLogEntryView> ActivityLog);
