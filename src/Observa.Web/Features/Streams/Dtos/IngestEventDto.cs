using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Dtos;

public sealed record IngestEventDto(
    DateTimeOffset OccurredAt,
    decimal Amount,
    IngestionSource Source);
