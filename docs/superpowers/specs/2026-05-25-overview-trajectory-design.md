# Overview — Net-worth Trajectory, YoY & Honest Projection

- **Date:** 2026-05-25
- **Status:** Approved (design) — pending implementation plan
- **Area:** `Observa.Web` Overview dashboard + `StreamAnalyticsService`

## Problem

The Overview answers "where have I been / where am I / where am I going", but the **"Looking ahead"** block is just three projected numbers (end-of-month, 3-month, year-end net) with no chart — it feels banal and only projects cash flow. There is no consolidated **net-worth** view, no **year-over-year** narrative ("2024 → +15% → 2025 → −5%"), and no way to see earning/spending at different time grains. Now that the Solana connector polls hourly, each Performance stream carries a real value time-series we can exploit.

## Goal

Enhance the Overview **in place** with a richer "where I came from / where I am / where I'm going" story:

1. **YoY strip** — net per year with % change vs the prior year.
2. **Net-worth trajectory** — a layered area (savings + assets) with history and an honest projection.
3. **Earn / Spend** — income vs expense with a day/week/month/year granularity toggle.
4. **Opening balance** — a one-time number so the savings layer reflects real savings, not just flow since tracking began.

## Data model & classification

Streams are already typed by `Direction`. The mapping is mechanical — nothing is classified by hand:

| Layer | Source streams | Definition |
|---|---|---|
| **Savings (cash)** | `Income`, `Outcome` | `openingBalance + Σ(Income − Outcome up to T) − assetCapital(T)` |
| **Assets (volatile)** | `Performance` (snapshot connectors) | `value(T) = Σ(Performance events up to T)` = market value |

**Net worth identity (no double-counting).** The user funds crypto **from registered income**, so capital that moved into assets must leave the cash layer:

```
NetWorth(T) = openingBalance + (Σ Income − Σ Outcome)(T) + AssetReturn(T)
            = SavingsLayer(T) + AssetsLayer(T)

  SavingsLayer(T) = openingBalance + (Σ Income − Σ Outcome)(T) − assetCapital(T)
  AssetsLayer(T)  = value(T)                                   (= capital + return)
```

Capital moving into assets is net-zero on net worth (leaves savings, enters assets); only **return** moves the total. This is the agreed reconciliation.

### Capital-over-time

`assetCapital(T)` is required to split the layers historically, but today only the *current* capital is stored (`ConnectorBindingState.CapitalBasisUsd`). Plan:

- **Going forward:** append `{At, CapitalUsd}` to a small per-binding capital history on each snapshot poll (parallel to the value series; fills in over time).
- **Before this feature:** approximate `assetCapital(T)` as the current capital for `T ≥ first asset event` (impact is small — capital is ~$5k).

## Components

### 1. YoY strip (`de dónde vengo`)
- One card per calendar year: **net cash flow** for the year (`Σ Income − Σ Outcome`) and **% change vs prior year** (▲/▼, green/red). Current year tagged "YTD".
- New analytics method returning `[{ Year, NetUsd, ChangePctVsPrior }]`.
- Richness scales with data history (backfill / future CSV import).

### 2. Net-worth trajectory (`dónde estoy + a dónde voy`)
- **Stacked area, two layers:** Savings (bottom) + Assets (top); the top edge is net worth.
- **History** to "now", then a **projection** region to the right of a vertical **"Hoy"** marker (shaded).
  - *Savings projection:* linear trend on recent net cash flow (extend existing `ComputeProjection` regression).
  - *Assets projection:* held flat at current value with a **± uncertainty band** — `sigma = assetVolatility · √(horizonMonths / windowMonths)`, where `assetVolatility` is the stddev of historical monthly asset-value % changes. Never extrapolates a crypto price.
- **Resolution:** monthly points with year-labelled x-axis. The Day/Week/Month/Year toggle lives on the Earn/Spend chart only — the trajectory is always monthly.
- Summary cards below: Savings now · Assets now · Projected net worth (with asset range).
- **Replaces** the "Looking ahead" block and **absorbs** the standalone "Running balance" chart (the cumulative-net + 6-month forecast it showed is a subset of this).

### 3. Earn / Spend with granularity (`cuándo gano/gasto`)
- Income (column) vs Outcome (column) + Net (line) aggregated at the selected grain.
- Toggle: **Day / Week / Month / Year**. `Performance` streams are excluded here (they belong to the trajectory, not cash flow).
- **Honest note per grain:** monthly streams (salary, rent, Patreon) appear as spikes on their event day; Day/Week is most useful for **Blofin (daily)** and asset value. The UI shows this note.
- New analytics method: aggregate Income/Outcome events into buckets for a given grain + window.

### 4. Opening balance
- A settings grain addressed by a fixed well-known key storing one USD number (per deployment) + a small settings input. (Not a singleton — it activates/deactivates on demand and rehydrates persisted state; "one instance" is a key convention.)
- Offsets the savings layer so net worth reflects real starting savings, not just flow since tracking began.

## Placement / layout (Dashboard Overview)

- **Remove:** "Looking ahead" numeric block; standalone "Running balance" section.
- **Add (top → bottom):** YoY strip → Net-worth trajectory → Earn/Spend → (existing) retrospective, monthly contribution, streams table.
- **Resolve overlap:** the existing "Yearly trend" stacked bars overlap the YoY strip — **remove** the yearly-trend bars; the YoY strip (totals + % narrative) plus the trajectory chart cover that ground.
- All within Overview (`_activeTab == 0`); no new tab. Accept that Overview grows; order it as the "back → present → ahead" narrative.

## New / changed types (indicative)

- `YearOverYearView(int Year, decimal NetUsd, decimal? ChangePctVsPrior, bool IsPartial)`
- `NetWorthPointView(DateTimeOffset At, decimal SavingsUsd, decimal AssetsUsd, bool IsProjected, decimal? BandLowUsd, decimal? BandHighUsd)`
- `EarnSpendPointView(string Label, decimal IncomeUsd, decimal OutcomeUsd)` + a `Granularity` enum (Day/Week/Month/Year).
- `OverviewSettings` (opening balance) grain with a fixed well-known key.
- `ConnectorBindingState`: add capital history (`List<(DateTimeOffset At, decimal CapitalUsd)>` or equivalent), appended on snapshot poll.

## Testing

- Net-worth series: layers sum to net worth; capital reconciliation removes double-count (savings drops by capital, assets add value, total = flow + return).
- Opening balance offsets every net-worth point by a constant.
- YoY %: correct deltas, first year null, partial current year flagged.
- Earn/Spend bucketing per grain (day/week/month/year) over a fixed `now`.
- Projection: savings follows trend slope; asset band widens with horizon; flat asset midpoint.
- All computations pure/static with injected `now`, mirroring existing analytics tests.

## Out of scope (future)

- CSV import / deeper historical backfill (separate roadmap item; this feature consumes whatever history exists).
- Per-stream anomaly flags, percentile projection bands, scenario builder (stay in What-if).
- Multi-currency.

## Resolved decisions

- Subject: **both layered** (savings + assets). 
- Projection: **honest by parts** (savings trend; assets flat + band).
- Reconciliation: crypto **funded from registered income** ⇒ subtract asset capital from savings.
- Placement: **enhance Overview in place**.
- Opening balance: **included**.
