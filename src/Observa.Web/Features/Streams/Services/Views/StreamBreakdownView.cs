using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

public sealed record StreamSummaryView(
    Guid Id,
    string Name,
    string Category,
    Direction Direction,
    StreamStatus Status);

public sealed record MonthlyStreamPointView(
    int Year,
    int Month,
    Guid StreamId,
    string StreamName,
    Direction Direction,
    decimal Amount)
{
    public string Label => new DateTimeOffset(Year, Month, 1, 0, 0, 0, TimeSpan.Zero).ToString("MMM yy");
}

public sealed record YearlyStreamPointView(
    int Year,
    Guid StreamId,
    string StreamName,
    Direction Direction,
    decimal Amount)
{
    public string Label => Year.ToString();
}
