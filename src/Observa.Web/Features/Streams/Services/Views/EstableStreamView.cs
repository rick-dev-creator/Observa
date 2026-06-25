using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

// A recurring cash-flow stream with its calendar (cadence + anchor) and the
// predicted-vs-real comparison for the last complete month.
public sealed record EstableStreamView(
    Guid StreamId,
    string Name,
    string Category,
    Direction Direction,
    bool IsFixed,
    Cadence Cadence,
    int Anchor,
    decimal Expected,
    decimal Actual);
