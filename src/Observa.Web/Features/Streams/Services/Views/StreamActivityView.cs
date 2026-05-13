using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

public sealed record StreamActivityView(
    Guid Id,
    string Name,
    StreamStatus Status,
    ReminderStatusView? ReminderStatus,
    IReadOnlyList<ActivityLogEntryView> ActivityLog);
