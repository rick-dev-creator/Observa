using Observa.Features.Streams.Enums;

namespace Observa.Features.Streams.Services.Views;

public sealed record WhatIfResultView(
    MonthSummaryView BaselineCurrentMonth,
    ProjectionView BaselineProjection,
    MonthSummaryView ScenarioCurrentMonth,
    ProjectionView ScenarioProjection,
    IReadOnlyList<ScenarioPointView> NetSeries,
    IReadOnlyList<StreamImpactView> StreamImpacts);

public sealed record ScenarioPointView(
    string Label,
    decimal Baseline,
    decimal Scenario,
    bool IsProjected);

public sealed record StreamImpactView(
    Guid StreamId,
    string Name,
    Direction Direction,
    decimal BaselineRecentAverage,
    decimal? ScenarioRecentAverage,
    decimal Delta);
