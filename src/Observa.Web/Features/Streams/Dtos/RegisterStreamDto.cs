using Observa.Features.Streams.Enums;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Dtos;

public sealed record RegisterStreamDto(
    string Name,
    string Category,
    Direction Direction,
    Recurrence? Schedule,
    decimal? ExpectedAmount);
