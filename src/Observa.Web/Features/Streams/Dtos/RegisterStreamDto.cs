namespace Observa.Features.Streams.Dtos;

public sealed record RegisterStreamDto(
    string Name,
    string Category,
    Direction Direction,
    Recurrence? Schedule,
    decimal? ExpectedAmount);
