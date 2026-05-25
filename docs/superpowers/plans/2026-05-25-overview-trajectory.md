# Overview Net-worth Trajectory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Overview's banal "Looking ahead" block with a layered net-worth trajectory (savings + assets), a year-over-year strip, an honest projection, and an earn/spend chart with a day/week/month/year toggle.

**Architecture:** Pure static compute methods on `StreamAnalyticsService` (TDD, injected `now`) feed new Razor view-components. The trajectory reuses the existing stable/volatile cumulative-balance machinery, redefining the "stable" (savings) layer to subtract asset capital and add an opening balance, so net worth = (income−outcome) + asset return with no double-counting. Asset capital over time is recorded per snapshot poll going forward.

**Tech Stack:** .NET 10 / C# 14, Blazor Server, Blazor-ApexCharts, Orleans grains, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-05-25-overview-trajectory-design.md`

---

## File structure

- `src/Observa.Web/Features/Streams/Grains/ConnectorBindingState.cs` — add capital history list (modify).
- `src/Observa.Web/Features/Streams/Grains/StreamGrain.cs` — append capital point on `SetConnectorSnapshotStateAsync` (modify).
- `src/Observa.Web/Features/Streams/Grains/OverviewSettingsGrain.cs` — opening-balance singleton (create).
- `src/Observa.Web/Features/Streams/Services/Views/CumulativeBalanceView.cs` — add `BandLow`/`BandHigh` (modify).
- `src/Observa.Web/Features/Streams/Services/Views/YearOverYearView.cs` — create.
- `src/Observa.Web/Features/Streams/Services/Views/EarnSpendView.cs` — create (record + `EarnSpendGranularity` enum).
- `src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs` — capital-at-T helper, net-worth trajectory, YoY, earn/spend (modify).
- `src/Observa.Web/Features/Streams/Components/NetWorthTrajectoryChart.razor` — create.
- `src/Observa.Web/Features/Streams/Components/YearOverYearStrip.razor` — create.
- `src/Observa.Web/Features/Streams/Components/EarnSpendChart.razor` — create.
- `src/Observa.Web/Features/Streams/Pages/Dashboard.razor` — layout rewrite (modify).
- `tests/Observa.Domain.Tests/Streams/*.cs` — analytics tests.

Existing tests live in `tests/Observa.Domain.Tests/Streams/AssetHoldingsTests.cs`; mirror their style (build `StreamGrainState` literals, call static methods with a fixed `now`).

---

## Task 1: Capital history on the connector binding

**Files:**
- Modify: `src/Observa.Web/Features/Streams/Grains/ConnectorBindingState.cs`
- Modify: `src/Observa.Web/Features/Streams/Grains/StreamGrain.cs:47-53` (`SetConnectorSnapshotStateAsync`)

- [ ] **Step 1: Add the capital-point type and list to the binding state**

In `ConnectorBindingState.cs`, add after the `CapitalBasisUsd` property (Id 4):

```csharp
    [Id(5)] public List<CapitalPoint> CapitalHistory { get; set; } = new();
```

And add this type in the same file (below the class, same namespace):

```csharp
[GenerateSerializer]
public sealed class CapitalPoint
{
    [Id(0)] public DateTimeOffset At { get; set; }
    [Id(1)] public decimal CapitalUsd { get; set; }
}
```

Do NOT add it to `ConnectorBindingState.From` / `ToDomain` — capital history is grain-owned runtime state, not part of the domain `ConnectorBinding`.

- [ ] **Step 2: Append a capital point on each snapshot poll**

In `StreamGrain.cs`, replace `SetConnectorSnapshotStateAsync` (currently lines 47-53):

```csharp
    public async Task SetConnectorSnapshotStateAsync(string? snapshotState, decimal? capitalBasisUsd)
    {
        if (state.State.Binding is null) return;
        state.State.Binding.SnapshotState = snapshotState;
        state.State.Binding.CapitalBasisUsd = capitalBasisUsd;
        if (capitalBasisUsd is { } cap)
        {
            var hist = state.State.Binding.CapitalHistory;
            // Coalesce: replace the last point if it is from the same day, else append.
            var now = DateTimeOffset.UtcNow;
            if (hist.Count > 0 && hist[^1].At.Date == now.Date)
                hist[^1] = new CapitalPoint { At = now, CapitalUsd = cap };
            else
                hist.Add(new CapitalPoint { At = now, CapitalUsd = cap });
        }
        await state.WriteStateAsync();
    }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Observa.Web/Observa.Web.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Observa.Web/Features/Streams/Grains/ConnectorBindingState.cs src/Observa.Web/Features/Streams/Grains/StreamGrain.cs
git commit -m "feat(streams): record connector capital basis over time"
```

---

## Task 2: `CapitalAt` helper (capital basis at an instant)

**Files:**
- Modify: `src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs`
- Test: `tests/Observa.Domain.Tests/Streams/CapitalHistoryTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/Observa.Domain.Tests/Streams/CapitalHistoryTests.cs`:

```csharp
using FluentAssertions;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class CapitalHistoryTests
{
    private static DateTimeOffset T(int day) => new(2026, 5, day, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CapitalAt_UsesLatestPointAtOrBeforeInstant()
    {
        var b = new ConnectorBindingState
        {
            ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = 300m,
            CapitalHistory = new()
            {
                new CapitalPoint { At = T(1), CapitalUsd = 100m },
                new CapitalPoint { At = T(5), CapitalUsd = 300m },
            },
        };

        StreamAnalyticsService.CapitalAt(b, T(3)).Should().Be(100m);
        StreamAnalyticsService.CapitalAt(b, T(9)).Should().Be(300m);
    }

    [Fact]
    public void CapitalAt_BeforeFirstPoint_IsZero()
    {
        var b = new ConnectorBindingState
        {
            ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = 100m,
            CapitalHistory = new() { new CapitalPoint { At = T(5), CapitalUsd = 100m } },
        };

        StreamAnalyticsService.CapitalAt(b, T(1)).Should().Be(0m);
    }

    [Fact]
    public void CapitalAt_NoHistory_FallsBackToCurrentCapital()
    {
        // Pre-feature streams have no recorded history; approximate with current capital.
        var b = new ConnectorBindingState
        {
            ConnectorId = "solana-main", ExternalRef = "mint", CapitalBasisUsd = 250m,
            CapitalHistory = new(),
        };

        StreamAnalyticsService.CapitalAt(b, T(3)).Should().Be(250m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~CapitalHistory" -v q`
Expected: FAIL — `CapitalAt` does not exist (compile error).

- [ ] **Step 3: Implement `CapitalAt`**

In `StreamAnalyticsService.cs`, add inside the class (near `BuildAssetHolding`):

```csharp
    // Capital basis at an instant. Uses the recorded history; before the first point capital is 0.
    // With no history (streams created before capital recording existed) we approximate with current capital.
    internal static decimal CapitalAt(Grains.ConnectorBindingState binding, DateTimeOffset t)
    {
        var hist = binding.CapitalHistory;
        if (hist is null || hist.Count == 0)
            return binding.CapitalBasisUsd ?? 0m;
        decimal capital = 0m;
        foreach (var p in hist.OrderBy(p => p.At))
        {
            if (p.At > t) break;
            capital = p.CapitalUsd;
        }
        return capital;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~CapitalHistory" -v q`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/Observa.Domain.Tests/Streams/CapitalHistoryTests.cs src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs
git commit -m "feat(analytics): CapitalAt(binding, t) with history + fallback"
```

---

## Task 3: Opening-balance singleton grain

**Files:**
- Create: `src/Observa.Web/Features/Streams/Grains/IOverviewSettingsGrain.cs`
- Create: `src/Observa.Web/Features/Streams/Grains/OverviewSettingsGrain.cs`

- [ ] **Step 1: Create the grain interface**

`IOverviewSettingsGrain.cs`:

```csharp
namespace Observa.Features.Streams.Grains;

public interface IOverviewSettingsGrain : IGrainWithStringKey
{
    Task<decimal> GetOpeningBalanceAsync();
    Task SetOpeningBalanceAsync(decimal openingBalanceUsd);
}
```

- [ ] **Step 2: Create the grain (mirror `StreamIndexGrain`)**

`OverviewSettingsGrain.cs`:

```csharp
namespace Observa.Features.Streams.Grains;

[GenerateSerializer]
public sealed class OverviewSettingsState
{
    [Id(0)] public decimal OpeningBalanceUsd { get; set; }
}

public sealed class OverviewSettingsGrain(
    [PersistentState("overview-settings")] IPersistentState<OverviewSettingsState> state)
    : Grain, IOverviewSettingsGrain
{
    public const string SingletonKey = "all";

    public Task<decimal> GetOpeningBalanceAsync() => Task.FromResult(state.State.OpeningBalanceUsd);

    public async Task SetOpeningBalanceAsync(decimal openingBalanceUsd)
    {
        state.State.OpeningBalanceUsd = openingBalanceUsd;
        await state.WriteStateAsync();
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Observa.Web/Observa.Web.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Observa.Web/Features/Streams/Grains/IOverviewSettingsGrain.cs src/Observa.Web/Features/Streams/Grains/OverviewSettingsGrain.cs
git commit -m "feat(streams): opening-balance settings singleton grain"
```

---

## Task 4: Net-worth trajectory series (savings − capital + opening, assets, projection band)

**Files:**
- Modify: `src/Observa.Web/Features/Streams/Services/Views/CumulativeBalanceView.cs`
- Modify: `src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs` (`ComputeCumulativeBalance` + a public async wrapper)
- Test: `tests/Observa.Domain.Tests/Streams/NetWorthTrajectoryTests.cs` (create)

- [ ] **Step 1: Add band fields to the point view**

In `CumulativeBalanceView.cs`, add two optional fields to the record (keep existing positional params, append at the end with defaults so existing call sites still compile):

```csharp
public sealed record CumulativeBalancePointView(
    string Label,
    DateTimeOffset Timestamp,
    decimal Balance,
    bool IsProjected,
    decimal StableBalance,
    decimal VolatileBalance,
    decimal? BandLow = null,    // net-worth low edge during projection (asset uncertainty)
    decimal? BandHigh = null);  // net-worth high edge during projection
```

(If the existing record has different positional names, keep them and only append `BandLow`/`BandHigh`.)

- [ ] **Step 2: Write the failing test**

Create `tests/Observa.Domain.Tests/Streams/NetWorthTrajectoryTests.cs`:

```csharp
using FluentAssertions;
using Observa.Connectors.Abstractions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class NetWorthTrajectoryTests
{
    private static DateTimeOffset M(int year, int month) => new(year, month, 10, 0, 0, 0, TimeSpan.Zero);

    private static StreamGrainState Income(string name, params (DateTimeOffset At, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = name, Category = "X", Direction = Direction.Income, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = e.At,
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Connector }).ToList(),
    };

    private static StreamGrainState Asset(string name, decimal capital, List<CapitalPoint> capHist,
        params (DateTimeOffset At, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = name, Category = "Crypto", Direction = Direction.Performance, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = e.At,
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Connector }).ToList(),
        Binding = new ConnectorBindingState { ConnectorId = "solana-main", ExternalRef = "m",
            CapitalBasisUsd = capital, CapitalHistory = capHist },
    };

    [Fact]
    public void Trajectory_SubtractsAssetCapitalFromSavings_NoDoubleCount()
    {
        var now = M(2026, 3);
        // Jan: +1000 income. Feb: bought 600 of an asset (capital 600, value 600).
        var states = new List<StreamGrainState>
        {
            Income("Salary", (M(2026,1), 1000m)),
            Asset("SOL", 600m, new() { new CapitalPoint { At = M(2026,2), CapitalUsd = 600m } },
                (M(2026,2), 600m)),
        };

        var pts = StreamAnalyticsService.ComputeNetWorthTrajectory(states, openingBalance: 0m, futureMonths: 0, now: now);

        var feb = pts.Single(p => p.Timestamp.Month == 2 && !p.IsProjected);
        // savings = income(1000) − capital(600) = 400 ; assets = value(600) ; net worth = 1000
        feb.StableBalance.Should().Be(400m);
        feb.VolatileBalance.Should().Be(600m);
        feb.Balance.Should().Be(1000m);
    }

    [Fact]
    public void Trajectory_AddsOpeningBalanceToSavings()
    {
        var now = M(2026, 2);
        var states = new List<StreamGrainState> { Income("Salary", (M(2026,1), 1000m)) };

        var pts = StreamAnalyticsService.ComputeNetWorthTrajectory(states, openingBalance: 5000m, futureMonths: 0, now: now);

        pts.First(p => p.Timestamp.Month == 1).StableBalance.Should().Be(6000m); // 5000 + 1000
    }

    [Fact]
    public void Trajectory_Projection_HoldsAssetsFlat_AndWidensBand()
    {
        var now = M(2026, 4);
        // Need >= 2 monthly asset-value changes so volatility (stddev) is non-zero.
        var states = new List<StreamGrainState>
        {
            Income("Salary", (M(2026,1), 1000m), (M(2026,2), 1000m), (M(2026,3), 1000m)),
            Asset("SOL", 600m, new() { new CapitalPoint { At = M(2026,1), CapitalUsd = 600m } },
                (M(2026,1), 600m), (M(2026,2), 60m), (M(2026,3), -40m)), // value 600 → 660 → 620
        };

        var pts = StreamAnalyticsService.ComputeNetWorthTrajectory(states, openingBalance: 0m, futureMonths: 3, now: now);

        var projected = pts.Where(p => p.IsProjected).ToList();
        projected.Should().HaveCount(3);
        // assets held flat at last value (620) across projection
        projected.Should().OnlyContain(p => p.VolatileBalance == 620m);
        // band brackets net worth and widens with horizon
        projected[0].BandHigh!.Value.Should().BeGreaterThan(projected[0].Balance);
        projected[2].BandHigh!.Value.Should().BeGreaterThan(projected[0].BandHigh!.Value);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~NetWorthTrajectory" -v q`
Expected: FAIL — `ComputeNetWorthTrajectory` does not exist.

- [ ] **Step 4: Implement `ComputeNetWorthTrajectory`**

In `StreamAnalyticsService.cs`, add:

```csharp
    // Net-worth trajectory: savings layer = opening + cumulative(income−outcome) − asset capital(T);
    // assets layer = cumulative Performance value. Projection holds assets flat at the last value with a
    // ±band from historical monthly asset-value volatility; savings continues on its recent trend.
    internal static IReadOnlyList<CumulativeBalancePointView> ComputeNetWorthTrajectory(
        IReadOnlyList<StreamGrainState> states, decimal openingBalance, int futureMonths, DateTimeOffset now)
    {
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var allEvents = states.SelectMany(s => s.Events.Select(e => (s.Direction, e.OccurredAt, e.Amount.Amount))).ToList();
        if (allEvents.Count == 0) return Array.Empty<CumulativeBalancePointView>();

        var first = allEvents.Min(e => e.OccurredAt);
        var firstMonth = new DateTimeOffset(first.Year, first.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var assetBindings = states.Where(s => s.Direction == Direction.Performance && s.Binding is not null)
            .Select(s => s.Binding!).ToList();

        decimal MonthFlow(DateTimeOffset mStart, Direction dir)
        {
            var mEnd = mStart.AddMonths(1);
            return allEvents.Where(e => e.Direction == dir && e.OccurredAt >= mStart && e.OccurredAt < mEnd)
                            .Sum(e => e.Amount);
        }

        var points = new List<CumulativeBalancePointView>();
        decimal runIncome = 0, runOutcome = 0, runValue = 0;
        var assetMonthlyPct = new List<decimal>(); // for volatility
        decimal prevValue = 0;

        for (var m = firstMonth; m <= anchor; m = m.AddMonths(1))
        {
            runIncome += MonthFlow(m, Direction.Income);
            runOutcome += MonthFlow(m, Direction.Outcome);
            runValue += MonthFlow(m, Direction.Performance);
            var capital = assetBindings.Sum(b => CapitalAt(b, m.AddMonths(1).AddTicks(-1)));

            var savings = Math.Round(openingBalance + runIncome - runOutcome - capital, 2);
            var assets = Math.Round(runValue, 2);
            points.Add(new CumulativeBalancePointView(
                m.ToString("MMM yy"), m, savings + assets, IsProjected: false, savings, assets));

            if (prevValue != 0 && runValue != 0) assetMonthlyPct.Add((runValue - prevValue) / prevValue);
            prevValue = runValue;
        }

        if (futureMonths > 0 && points.Count > 0)
        {
            // savings trend from the last up-to-6 complete months
            var window = points.TakeLast(Math.Min(7, points.Count)).ToList();
            var savingsSlope = window.Count > 1 ? (window[^1].StableBalance - window[0].StableBalance) / (window.Count - 1) : 0m;
            var lastSavings = points[^1].StableBalance;
            var flatAssets = points[^1].VolatileBalance;
            var sigmaMonthly = assetMonthlyPct.Count > 1 ? StdDev(assetMonthlyPct.ToArray()) : 0m;

            for (var i = 1; i <= futureMonths; i++)
            {
                var d = anchor.AddMonths(i);
                var savings = Math.Round(lastSavings + savingsSlope * i, 2);
                var nw = savings + flatAssets;
                var band = Math.Round(flatAssets * sigmaMonthly * (decimal)Math.Sqrt(i), 2);
                points.Add(new CumulativeBalancePointView(
                    d.ToString("MMM yy"), d, nw, IsProjected: true, savings, flatAssets,
                    BandLow: Math.Round(nw - band, 2), BandHigh: Math.Round(nw + band, 2)));
            }
        }

        return points;
    }

    public async Task<IReadOnlyList<CumulativeBalancePointView>> GetNetWorthTrajectoryAsync(
        int futureMonths, CancellationToken ct)
    {
        var states = await LoadAllAsync(ct);
        var opening = await grains.GetGrain<Grains.IOverviewSettingsGrain>(Grains.OverviewSettingsGrain.SingletonKey)
            .GetOpeningBalanceAsync();
        return ComputeNetWorthTrajectory(states, opening, futureMonths, DateTimeOffset.UtcNow);
    }
```

`StdDev` already exists in this class (used by `ComputeProjection`). `grains` is the injected `IGrainFactory` constructor parameter.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~NetWorthTrajectory" -v q`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Observa.Web/Features/Streams/Services/Views/CumulativeBalanceView.cs src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs tests/Observa.Domain.Tests/Streams/NetWorthTrajectoryTests.cs
git commit -m "feat(analytics): net-worth trajectory with capital reconciliation, opening balance & projection band"
```

---

## Task 5: Year-over-year series

**Files:**
- Create: `src/Observa.Web/Features/Streams/Services/Views/YearOverYearView.cs`
- Modify: `src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs`
- Test: `tests/Observa.Domain.Tests/Streams/YearOverYearTests.cs` (create)

- [ ] **Step 1: Create the view**

`YearOverYearView.cs`:

```csharp
namespace Observa.Features.Streams.Services.Views;

public sealed record YearOverYearView(
    int Year,
    decimal NetUsd,                 // Σ Income − Σ Outcome for the year (cash flow earned/saved)
    decimal? ChangePctVsPrior,      // fraction vs previous year's net; null for the first year
    bool IsPartial);                // true for the current (incomplete) year
```

- [ ] **Step 2: Write the failing test**

Create `tests/Observa.Domain.Tests/Streams/YearOverYearTests.cs`:

```csharp
using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Domain.Tests.Streams;

public sealed class YearOverYearTests
{
    private static StreamGrainState Stream(Direction dir, params (int Year, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = "S", Category = "X", Direction = dir, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(e.Year, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Manual }).ToList(),
    };

    [Fact]
    public void YearOverYear_ComputesNetAndPercentChange()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income, (2024, 100m), (2025, 115m), (2026, 60m)),
            Stream(Direction.Outcome, (2024, 0m), (2025, 0m), (2026, 0m)),
        };

        var rows = StreamAnalyticsService.ComputeYearOverYear(states, now);

        rows.Should().HaveCount(3);
        rows[0].Should().BeEquivalentTo(new { Year = 2024, NetUsd = 100m, ChangePctVsPrior = (decimal?)null, IsPartial = false });
        rows[1].ChangePctVsPrior.Should().BeApproximately(0.15m, 0.0001m); // +15%
        rows[2].IsPartial.Should().BeTrue();                               // current year
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~YearOverYear" -v q`
Expected: FAIL — `ComputeYearOverYear` does not exist.

- [ ] **Step 4: Implement `ComputeYearOverYear`**

In `StreamAnalyticsService.cs`:

```csharp
    internal static IReadOnlyList<YearOverYearView> ComputeYearOverYear(
        IReadOnlyList<StreamGrainState> states, DateTimeOffset now)
    {
        var byYear = new SortedDictionary<int, decimal>();
        foreach (var s in states)
        {
            if (s.Direction == Direction.Performance) continue; // cash flow only
            var sign = s.Direction == Direction.Outcome ? -1m : 1m;
            foreach (var e in s.Events)
                byYear[e.OccurredAt.Year] = byYear.GetValueOrDefault(e.OccurredAt.Year) + sign * e.Amount.Amount;
        }

        var rows = new List<YearOverYearView>();
        decimal? prev = null;
        foreach (var (year, net) in byYear)
        {
            decimal? chg = prev is { } p && p != 0 ? Math.Round((net - p) / Math.Abs(p), 4) : null;
            rows.Add(new YearOverYearView(year, Math.Round(net, 2), chg, IsPartial: year == now.Year));
            prev = net;
        }
        return rows;
    }

    public async Task<IReadOnlyList<YearOverYearView>> GetYearOverYearAsync(CancellationToken ct) =>
        ComputeYearOverYear(await LoadAllAsync(ct), DateTimeOffset.UtcNow);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~YearOverYear" -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Observa.Web/Features/Streams/Services/Views/YearOverYearView.cs src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs tests/Observa.Domain.Tests/Streams/YearOverYearTests.cs
git commit -m "feat(analytics): year-over-year net with percent change"
```

---

## Task 6: Earn/Spend by granularity

**Files:**
- Create: `src/Observa.Web/Features/Streams/Services/Views/EarnSpendView.cs`
- Modify: `src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs`
- Test: `tests/Observa.Domain.Tests/Streams/EarnSpendTests.cs` (create)

- [ ] **Step 1: Create the view + enum**

`EarnSpendView.cs`:

```csharp
namespace Observa.Features.Streams.Services.Views;

public enum EarnSpendGranularity { Day, Week, Month, Year }

public sealed record EarnSpendPointView(string Label, DateTimeOffset BucketStart, decimal IncomeUsd, decimal OutcomeUsd)
{
    public decimal NetUsd => IncomeUsd - OutcomeUsd;
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/Observa.Domain.Tests/Streams/EarnSpendTests.cs`:

```csharp
using FluentAssertions;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;
using Observa.Features.Streams.Services.Views;

namespace Observa.Domain.Tests.Streams;

public sealed class EarnSpendTests
{
    private static StreamGrainState Stream(Direction dir, params (DateTimeOffset At, decimal Amt)[] ev) => new()
    {
        Id = Guid.NewGuid(), Name = "S", Category = "X", Direction = dir, Status = StreamStatus.Active,
        Events = ev.Select(e => new FlowEventSnapshot { Id = Guid.NewGuid(), OccurredAt = e.At,
            Amount = new MoneyState { Amount = e.Amt }, Source = IngestionSource.Manual }).ToList(),
    };

    [Fact]
    public void EarnSpend_BucketsByMonth_ExcludesPerformance()
    {
        var now = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income,  (new(2026,5,2,0,0,0,TimeSpan.Zero), 1000m)),
            Stream(Direction.Outcome, (new(2026,5,3,0,0,0,TimeSpan.Zero), 400m)),
            Stream(Direction.Performance, (new(2026,5,4,0,0,0,TimeSpan.Zero), 50m)), // ignored
        };

        var pts = StreamAnalyticsService.ComputeEarnSpend(states, EarnSpendGranularity.Month, periods: 3, now);

        var may = pts.Last();
        may.IncomeUsd.Should().Be(1000m);
        may.OutcomeUsd.Should().Be(400m);
        may.NetUsd.Should().Be(600m);
    }

    [Fact]
    public void EarnSpend_BucketsByDay()
    {
        var now = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
        var states = new List<StreamGrainState>
        {
            Stream(Direction.Income, (new(2026,5,15,9,0,0,TimeSpan.Zero), 30m), (new(2026,5,15,18,0,0,TimeSpan.Zero), 20m)),
        };

        var pts = StreamAnalyticsService.ComputeEarnSpend(states, EarnSpendGranularity.Day, periods: 7, now);

        pts.Should().HaveCount(7);
        pts.Last().IncomeUsd.Should().Be(50m); // both same-day events in the last bucket
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~EarnSpend" -v q`
Expected: FAIL — `ComputeEarnSpend` does not exist.

- [ ] **Step 4: Implement `ComputeEarnSpend`**

In `StreamAnalyticsService.cs`:

```csharp
    internal static IReadOnlyList<EarnSpendPointView> ComputeEarnSpend(
        IReadOnlyList<StreamGrainState> states, EarnSpendGranularity grain, int periods, DateTimeOffset now)
    {
        DateTimeOffset StartOf(DateTimeOffset t) => grain switch
        {
            EarnSpendGranularity.Day   => new DateTimeOffset(t.Year, t.Month, t.Day, 0, 0, 0, TimeSpan.Zero),
            EarnSpendGranularity.Week  => new DateTimeOffset(t.Year, t.Month, t.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-(int)t.DayOfWeek),
            EarnSpendGranularity.Month => new DateTimeOffset(t.Year, t.Month, 1, 0, 0, 0, TimeSpan.Zero),
            _                          => new DateTimeOffset(t.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        DateTimeOffset Advance(DateTimeOffset s, int n) => grain switch
        {
            EarnSpendGranularity.Day   => s.AddDays(n),
            EarnSpendGranularity.Week  => s.AddDays(7 * n),
            EarnSpendGranularity.Month => s.AddMonths(n),
            _                          => s.AddYears(n),
        };
        string Label(DateTimeOffset s) => grain switch
        {
            EarnSpendGranularity.Day   => s.ToString("d MMM"),
            EarnSpendGranularity.Week  => "w/" + s.ToString("d MMM"),
            EarnSpendGranularity.Month => s.ToString("MMM yy"),
            _                          => s.Year.ToString(),
        };

        var anchor = StartOf(now);
        var buckets = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var i = periods - 1; i >= 0; i--)
        {
            var start = Advance(anchor, -i);
            buckets.Add((start, Advance(start, 1)));
        }

        return buckets.Select(b =>
        {
            decimal inc = 0, outc = 0;
            foreach (var s in states)
            {
                if (s.Direction == Direction.Performance) continue;
                foreach (var e in s.Events)
                {
                    if (e.OccurredAt < b.Start || e.OccurredAt >= b.End) continue;
                    if (s.Direction == Direction.Income) inc += e.Amount.Amount;
                    else outc += e.Amount.Amount;
                }
            }
            return new EarnSpendPointView(Label(b.Start), b.Start, Math.Round(inc, 2), Math.Round(outc, 2));
        }).ToList();
    }

    public async Task<IReadOnlyList<EarnSpendPointView>> GetEarnSpendAsync(
        EarnSpendGranularity grain, int periods, CancellationToken ct) =>
        ComputeEarnSpend(await LoadAllAsync(ct), grain, periods, DateTimeOffset.UtcNow);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Observa.Domain.Tests/Observa.Domain.Tests.csproj --filter "FullyQualifiedName~EarnSpend" -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Observa.Web/Features/Streams/Services/Views/EarnSpendView.cs src/Observa.Web/Features/Streams/Services/StreamAnalyticsService.cs tests/Observa.Domain.Tests/Streams/EarnSpendTests.cs
git commit -m "feat(analytics): earn/spend buckets by day/week/month/year"
```

---

## Task 7: YearOverYearStrip component

**Files:**
- Create: `src/Observa.Web/Features/Streams/Components/YearOverYearStrip.razor`

- [ ] **Step 1: Create the component**

```razor
@using Observa.Features.Streams.Services.Views
@using System.Globalization

<div class="mb-3 text-xs uppercase tracking-wide text-zinc-500">De dónde vengo · neto por año</div>
<div class="grid grid-cols-2 md:grid-cols-5 gap-3 mb-6">
    @foreach (var r in Rows)
    {
        <div class="border border-zinc-800 rounded-lg p-3 bg-zinc-900/40">
            <div class="text-xs text-zinc-500">@r.Year@(r.IsPartial ? " · YTD" : "")</div>
            <div class="text-lg font-semibold text-zinc-100 tabular-nums">@Fmt(r.NetUsd)</div>
            <div class="text-xs tabular-nums @ChgColor(r.ChangePctVsPrior)">@ChgText(r.ChangePctVsPrior)</div>
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<YearOverYearView> Rows { get; set; } = [];
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string Fmt(decimal v) => (v < 0 ? "−" : "+") + "$" + Math.Abs(v).ToString("N0", Inv);
    private static string ChgText(decimal? p) => p is { } v ? (v >= 0 ? "▲ +" : "▼ ") + (v * 100).ToString("0", Inv) + "%" : "—";
    private static string ChgColor(decimal? p) => p is { } v ? (v >= 0 ? "text-emerald-400" : "text-rose-400") : "text-zinc-600";
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Observa.Web/Observa.Web.csproj -v q`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/Observa.Web/Features/Streams/Components/YearOverYearStrip.razor
git commit -m "feat(dashboard): year-over-year strip component"
```

---

## Task 8: NetWorthTrajectoryChart component

**Files:**
- Create: `src/Observa.Web/Features/Streams/Components/NetWorthTrajectoryChart.razor`

This adapts the existing `CumulativeBalanceChart.razor` pattern: stacked `Stable`+`Volatile` areas, datetime x-axis, "Today" annotation. Add a dashed range-area for `BandLow`/`BandHigh` in the projection.

- [ ] **Step 1: Create the component**

```razor
@using ApexCharts
@using Observa.Features.Streams.Services.Views

<div class="w-full">
    <ApexChart TItem="CumulativeBalancePointView" Options="@_options" Height="300" Debug="false">
        <ApexPointSeries TItem="CumulativeBalancePointView" Items="Data" Name="Ahorro (cash)"
                         SeriesType="SeriesType.Area"
                         XValue="@(p => (object)p.Timestamp.ToUnixTimeMilliseconds())"
                         YValue="@(p => p.StableBalance)" />
        <ApexPointSeries TItem="CumulativeBalancePointView" Items="Data" Name="Activos"
                         SeriesType="SeriesType.Area"
                         XValue="@(p => (object)p.Timestamp.ToUnixTimeMilliseconds())"
                         YValue="@(p => p.VolatileBalance)" />
        @if (Data.Any(p => p.BandHigh is not null))
        {
            <ApexPointSeries TItem="CumulativeBalancePointView" Items="Data" Name="Proy. alta"
                             SeriesType="SeriesType.Line"
                             XValue="@(p => (object)p.Timestamp.ToUnixTimeMilliseconds())"
                             YValue="@(p => p.BandHigh ?? p.Balance)" />
            <ApexPointSeries TItem="CumulativeBalancePointView" Items="Data" Name="Proy. baja"
                             SeriesType="SeriesType.Line"
                             XValue="@(p => (object)p.Timestamp.ToUnixTimeMilliseconds())"
                             YValue="@(p => p.BandLow ?? p.Balance)" />
        }
    </ApexChart>
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<CumulativeBalancePointView> Data { get; set; } = [];
    private ApexChartOptions<CumulativeBalancePointView> _options = null!;

    protected override void OnParametersSet()
    {
        var lastHistorical = Data.LastOrDefault(p => !p.IsProjected);
        _options = new ApexChartOptions<CumulativeBalancePointView>
        {
            Chart = new Chart
            {
                Background = "transparent", ForeColor = "#a1a1aa",
                Toolbar = new Toolbar { Show = false },
                Animations = new Animations { Enabled = true, Speed = 400 },
                FontFamily = "ui-sans-serif, system-ui",
                Zoom = new Zoom { Enabled = false },
                Stacked = true, // only area series stack; line series (band) plot independently
            },
            Colors = new List<string> { "#38bdf8", "#34d399", "#34d399", "#34d399" },
            Stroke = new Stroke
            {
                Curve = new List<Curve> { Curve.Smooth, Curve.Smooth, Curve.Straight, Curve.Straight },
                Width = new List<double> { 2.5, 2.5, 1, 1 },
                DashArray = new List<double> { 0, 0, 4, 4 },
            },
            Fill = new Fill
            {
                Type = new List<FillType> { FillType.Gradient, FillType.Gradient, FillType.Solid, FillType.Solid },
                Gradient = new FillGradient { ShadeIntensity = 1, OpacityFrom = 0.4, OpacityTo = 0.05, Stops = new List<double> { 0, 100 } },
                Opacity = new List<double> { 0.4, 0.4, 0, 0 },
            },
            Markers = new Markers { Size = 0 },
            Grid = new Grid { BorderColor = "#27272a", StrokeDashArray = 3 },
            Xaxis = new XAxis
            {
                Type = XAxisType.Datetime,
                AxisBorder = new AxisBorder { Color = "#27272a" },
                Labels = new XAxisLabels { Rotate = 0, HideOverlappingLabels = true, Format = "MMM yy" },
            },
            Yaxis = new List<YAxis> { new() { Labels = new YAxisLabels { Formatter = "function(v){return '$'+Math.round(v).toLocaleString('en-US');}" } } },
            Tooltip = new Tooltip { Theme = Mode.Dark, Shared = true, Intersect = false,
                Y = new TooltipY { Formatter = "function(v){return '$'+Math.round(v).toLocaleString('en-US');}" } },
            Legend = new Legend { Show = true, Position = LegendPosition.Top, HorizontalAlign = Align.Left },
            DataLabels = new DataLabels { Enabled = false },
            Annotations = lastHistorical is not null
                ? new Annotations { Xaxis = new List<AnnotationsXAxis> { new()
                    {
                        X = lastHistorical.Timestamp.ToUnixTimeMilliseconds(),
                        BorderColor = "#71717a", StrokeDashArray = 5,
                        Label = new Label { Text = "Hoy", BorderColor = "#71717a",
                            Style = new Style { Background = "#27272a", Color = "#e4e4e7", FontSize = "11px" } },
                    } } }
                : null,
        };
    }
}
```

- [ ] **Step 2: Build, then visually verify the band renders correctly**

Run: `dotnet build src/Observa.Web/Observa.Web.csproj -v q`
Expected: `Build succeeded`.

**Behavioral risk to check at the visual step (Task 11):** with `Stacked = true`, some Blazor-ApexCharts versions also stack *line* series, which would push the dashed band lines to the wrong height. If the band lines do not sit just above/below the net-worth top edge in the projection region, fix by either: (a) dropping the two band line series here and instead showing the projected range numerically in the section header (`$low – $high`), or (b) rendering the band as a single `RangeArea` series fed by `{BandLow, BandHigh}`. Prefer (a) if RangeArea is unavailable in the installed package version.

- [ ] **Step 3: Commit**

```bash
git add src/Observa.Web/Features/Streams/Components/NetWorthTrajectoryChart.razor
git commit -m "feat(dashboard): net-worth trajectory chart component"
```

---

## Task 9: EarnSpendChart component (with granularity toggle)

**Files:**
- Create: `src/Observa.Web/Features/Streams/Components/EarnSpendChart.razor`

- [ ] **Step 1: Create the component**

```razor
@using ApexCharts
@using Observa.Features.Streams.Services.Views

<div class="flex flex-wrap items-center justify-between gap-3 mb-3">
    <div>
        <h2 class="text-sm uppercase tracking-wide text-zinc-400">Gano / Gasto</h2>
        <p class="text-xs text-zinc-500">Ingreso vs gasto por periodo · la línea es el neto</p>
    </div>
    <div class="inline-flex rounded-md border border-zinc-700 overflow-hidden text-xs">
        @foreach (var g in _grains)
        {
            <button class="px-3 py-1.5 @(g == Granularity ? "bg-zinc-800 text-zinc-100" : "text-zinc-400")"
                    @onclick="@(() => OnPick(g))">@Label(g)</button>
        }
    </div>
</div>
<ApexChart TItem="EarnSpendPointView" Options="@_options" Height="260" Debug="false">
    <ApexPointSeries TItem="EarnSpendPointView" Items="Data" Name="Ingreso" SeriesType="SeriesType.Bar"
                     XValue="@(p => p.Label)" YValue="@(p => p.IncomeUsd)" />
    <ApexPointSeries TItem="EarnSpendPointView" Items="Data" Name="Gasto" SeriesType="SeriesType.Bar"
                     XValue="@(p => p.Label)" YValue="@(p => p.OutcomeUsd)" />
    <ApexPointSeries TItem="EarnSpendPointView" Items="Data" Name="Neto" SeriesType="SeriesType.Line"
                     XValue="@(p => p.Label)" YValue="@(p => p.NetUsd)" />
</ApexChart>
<p class="text-[11px] text-zinc-600 mt-2">@Note(Granularity)</p>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<EarnSpendPointView> Data { get; set; } = [];
    [Parameter, EditorRequired] public EarnSpendGranularity Granularity { get; set; }
    [Parameter] public EventCallback<EarnSpendGranularity> GranularityChanged { get; set; }

    private static readonly EarnSpendGranularity[] _grains =
        { EarnSpendGranularity.Day, EarnSpendGranularity.Week, EarnSpendGranularity.Month, EarnSpendGranularity.Year };
    private ApexChartOptions<EarnSpendPointView> _options = Build();

    private Task OnPick(EarnSpendGranularity g) => GranularityChanged.InvokeAsync(g);

    private static string Label(EarnSpendGranularity g) => g switch
    {
        EarnSpendGranularity.Day => "Día", EarnSpendGranularity.Week => "Semana",
        EarnSpendGranularity.Month => "Mes", _ => "Año",
    };
    private static string Note(EarnSpendGranularity g) => g switch
    {
        EarnSpendGranularity.Day => "Día: visible Blofin (diario) y picos puntuales (renta). Sueldo/Patreon son mensuales → pico en su día.",
        EarnSpendGranularity.Week => "Semana: agrega lo diario; la renta aparece como pico ~mensual.",
        EarnSpendGranularity.Month => "Mes: la granularidad natural de tus streams principales.",
        _ => "Año: comparativa anual de ingreso vs gasto.",
    };

    private static ApexChartOptions<EarnSpendPointView> Build() => new()
    {
        Chart = new Chart { Background = "transparent", ForeColor = "#a1a1aa", Toolbar = new Toolbar { Show = false },
            FontFamily = "ui-sans-serif, system-ui", Zoom = new Zoom { Enabled = false } },
        Colors = new List<string> { "#34d399", "#fb7185", "#a1a1aa" },
        Stroke = new Stroke { Width = new List<double> { 0, 0, 2 }, Curve = new List<Curve> { Curve.Smooth, Curve.Smooth, Curve.Smooth } },
        PlotOptions = new PlotOptions { Bar = new PlotOptionsBar { ColumnWidth = "60%", BorderRadius = 2 } },
        Grid = new Grid { BorderColor = "#27272a", StrokeDashArray = 3 },
        Yaxis = new List<YAxis> { new() { Labels = new YAxisLabels { Formatter = "function(v){return '$'+Math.round(v).toLocaleString('en-US');}" } } },
        Tooltip = new Tooltip { Theme = Mode.Dark },
        Legend = new Legend { Show = true, Position = LegendPosition.Top, HorizontalAlign = Align.Left },
        DataLabels = new DataLabels { Enabled = false },
    };
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Observa.Web/Observa.Web.csproj -v q`
Expected: `Build succeeded`. (If `PlotOptionsBar.ColumnWidth`/`BorderRadius` names differ in the installed Blazor-ApexCharts version, adjust to the available property names — confirm via the package's `PlotOptionsBar` type.)

- [ ] **Step 3: Commit**

```bash
git add src/Observa.Web/Features/Streams/Components/EarnSpendChart.razor
git commit -m "feat(dashboard): earn/spend chart with granularity toggle"
```

---

## Task 10: Wire components into the Overview + opening-balance input

**Files:**
- Modify: `src/Observa.Web/Features/Streams/Pages/Dashboard.razor`

- [ ] **Step 1: Replace the "Looking ahead" section and remove "Running balance" / "Yearly trend"**

In `Dashboard.razor`:
1. Delete the `<section>` whose header is `Looking ahead` (the block starting at line ~90, the right column of the `grid lg:grid-cols-3`). Make the "This month so far" section full width (`lg:col-span-3` or drop the grid wrapper).
2. Delete the standalone `Running balance` section (header at line ~176) and the `Yearly trend` section (header at line ~186).
3. Immediately after the `<StreamFilter .../>` line, insert:

```razor
    <YearOverYearStrip Rows="_yoy" />

    <section class="border border-zinc-800 rounded p-4 sm:p-5 mb-6">
        <div class="flex flex-wrap items-end justify-between gap-3 mb-1">
            <div>
                <h2 class="text-sm uppercase tracking-wide text-zinc-400">Trayectoria de patrimonio</h2>
                <p class="text-xs text-zinc-500">Apilado: ahorro + activos = patrimonio · a la derecha de "Hoy" = proyección</p>
            </div>
            <div class="text-right">
                <div class="text-2xl font-semibold text-zinc-100 tabular-nums">@FormatMoney(_netWorthNow)</div>
                <div class="text-xs text-zinc-500">patrimonio hoy</div>
            </div>
        </div>
        <NetWorthTrajectoryChart @key="@($"nw-{_filterKey}")" Data="_trajectory" />
    </section>

    <section class="border border-zinc-800 rounded p-4 sm:p-5 mb-6">
        <EarnSpendChart Data="_earnSpend" Granularity="_grain" GranularityChanged="OnGrainChangedAsync" />
    </section>
```

- [ ] **Step 2: Add the opening-balance input** (place inside the existing "Where you've been" / settings area, or a small section near the top)

```razor
    <div class="flex items-center gap-2 mb-6 text-sm">
        <span class="text-zinc-500">Saldo de apertura</span>
        <input type="number" step="100" value="@_openingBalance"
               @onchange="OnOpeningBalanceChangedAsync"
               class="w-32 bg-zinc-900 border border-zinc-700 rounded px-2 py-1 text-zinc-100 tabular-nums" />
        <span class="text-xs text-zinc-600">ancla la capa de ahorro a tu ahorro real</span>
    </div>
```

- [ ] **Step 3: Add the code-behind fields and loaders**

In the `@code` block, add fields:

```csharp
    private IReadOnlyList<YearOverYearView> _yoy = [];
    private IReadOnlyList<CumulativeBalancePointView> _trajectory = [];
    private IReadOnlyList<EarnSpendPointView> _earnSpend = [];
    private EarnSpendGranularity _grain = EarnSpendGranularity.Month;
    private decimal _openingBalance;
    private decimal _netWorthNow;
```

In the data-loading method (where `_projection`, `_balance` etc. are loaded), replace the `_projection` / `_balance` loads with:

```csharp
        _yoy = await Analytics.GetYearOverYearAsync(CancellationToken.None);
        _trajectory = await Analytics.GetNetWorthTrajectoryAsync(6, CancellationToken.None);
        _netWorthNow = _trajectory.LastOrDefault(p => !p.IsProjected)?.Balance ?? 0m;
        _openingBalance = await Grains.GetGrain<IOverviewSettingsGrain>(OverviewSettingsGrain.SingletonKey).GetOpeningBalanceAsync();
        _earnSpend = await Analytics.GetEarnSpendAsync(_grain, EarnSpendPeriods(_grain), CancellationToken.None);
```

Add helper methods + handlers:

```csharp
    private static int EarnSpendPeriods(EarnSpendGranularity g) => g switch
    {
        EarnSpendGranularity.Day => 30, EarnSpendGranularity.Week => 12,
        EarnSpendGranularity.Month => 12, _ => 5,
    };

    private async Task OnGrainChangedAsync(EarnSpendGranularity g)
    {
        _grain = g;
        _earnSpend = await Analytics.GetEarnSpendAsync(g, EarnSpendPeriods(g), CancellationToken.None);
    }

    private async Task OnOpeningBalanceChangedAsync(ChangeEventArgs e)
    {
        if (decimal.TryParse(e.Value?.ToString(), out var v))
        {
            await Grains.GetGrain<IOverviewSettingsGrain>(OverviewSettingsGrain.SingletonKey).SetOpeningBalanceAsync(v);
            _openingBalance = v;
            _trajectory = await Analytics.GetNetWorthTrajectoryAsync(6, CancellationToken.None);
            _netWorthNow = _trajectory.LastOrDefault(p => !p.IsProjected)?.Balance ?? 0m;
        }
    }
```

Add `@using` / `@inject` as needed at the top of the file:

```razor
@using Observa.Features.Streams.Services.Views
@using Observa.Features.Streams.Grains
@inject IGrainFactory Grains
```

(If `IGrainFactory` is already injected under another name, reuse it. Remove now-unused `_projection`, `_balance`, and their loader lines + the deleted views' fields.)

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/Observa.Web/Observa.Web.csproj -v q`
Expected: `Build succeeded`. Fix any leftover references to removed fields (`_projection`, `_balance`) or components.

- [ ] **Step 5: Commit**

```bash
git add src/Observa.Web/Features/Streams/Pages/Dashboard.razor
git commit -m "feat(dashboard): wire trajectory, YoY, earn/spend & opening balance into Overview"
```

---

## Task 11: Full verification & visual check

- [ ] **Step 1: Run the whole test suite**

Run: `dotnet test -v q`
Expected: all projects PASS (Domain count increased by the new tests; no regressions).

- [ ] **Step 2: Build the Web project clean**

Run: `dotnet build src/Observa.Web/Observa.Web.csproj -v q`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Run locally and visually verify the Overview**

Run: `dotnet run --project src/Observa.AppHost` (or the project's run skill), open the dashboard, confirm: YoY strip shows years with %; trajectory shows stacked savings+assets with a "Hoy" divider and dashed projection band; earn/spend toggles between Día/Semana/Mes/Año; opening-balance input shifts the savings layer. No console errors.

- [ ] **Step 4: Commit any fixes from the visual check**

```bash
git add -A && git commit -m "fix(dashboard): trajectory overview polish from visual check"
```

---

## Notes for the implementer

- All analytics methods take an explicit `now` and are pure/static — test them directly, never through Orleans.
- The deployed NAS image (`fix-poll`) already contains the poll-interval fix and the Holdings tab; this plan's commits stack on top. Releasing is a separate step (commit history → `:main` via CI).
- `StdDev` and `LoadAllAsync` already exist on `StreamAnalyticsService`; reuse them.
- Blazor-ApexCharts property names (e.g. on `PlotOptionsBar`, `Fill.Opacity`) can vary by package version — if a property doesn't exist, check the installed `ApexCharts` package types and use the nearest equivalent rather than inventing one.
