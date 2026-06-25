namespace Observa.Features.Streams.Services.Views;

// A single cumulative value at a point in time, for a one-line trajectory chart.
public sealed record TrajectoryPointView(DateTimeOffset Date, decimal Value);
