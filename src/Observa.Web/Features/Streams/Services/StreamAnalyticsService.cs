using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services.Views;

namespace Observa.Features.Streams.Services;

public sealed class StreamAnalyticsService(IGrainFactory grains)
{
    private const decimal VolatilityThreshold = 0.30m; // stddev/mean > 30% → Volatile
    private const decimal SteadyThresholdPctOfAvg = 0.05m;

    // Income adds, Outcome subtracts, Performance is already stored signed so it adds as-is.
    internal static decimal SignedNet(Direction direction, decimal amount) => direction switch
    {
        Direction.Income => amount,
        Direction.Outcome => -amount,
        Direction.Performance => amount,
        _ => 0m,
    };

    public async Task<MonthSummaryView> GetCurrentMonthAsync(CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeCurrentMonth(states);
    }

    public async Task<IReadOnlyList<MonthlyAggregateView>> GetMonthlyHistoryAsync(int months, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeMonthlyHistory(states, months);
    }

    public async Task<IReadOnlyList<MonthlyStreamPointView>> GetMonthlyHistoryByStreamAsync(int months, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeMonthlyHistoryByStream(states, months);
    }

    public async Task<IReadOnlyList<StreamTrendView>> GetStreamTrendsAsync(int sparklineMonths, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeStreamTrends(states, sparklineMonths);
    }

    public async Task<ProjectionView> GetProjectionAsync(CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeProjection(states);
    }

    public async Task<RetrospectiveView> GetRetrospectiveAsync(CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeRetrospective(states);
    }

    public async Task<IReadOnlyList<CumulativeBalancePointView>> GetCumulativeBalanceAsync(int futureMonths, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeCumulativeBalance(states, futureMonths);
    }

    public async Task<IReadOnlyList<YearlyAggregateView>> GetYearlyHistoryAsync(CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeYearlyHistory(states);
    }

    public async Task<IReadOnlyList<YearlyStreamPointView>> GetYearlyHistoryByStreamAsync(CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeYearlyHistoryByStream(states);
    }

    public async Task<IReadOnlyList<StreamSummaryView>> GetStreamSummariesAsync(CancellationToken ct)
    {
        var states = await LoadAllAsync(ct);
        return states
            .OrderByDescending(s => s.Direction == Direction.Income)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new StreamSummaryView(s.Id, s.Name, s.Category, s.Direction, s.Status))
            .ToList();
    }

    private const int SparklinePoints = 24;          // samples across the holding's recent window
    private static readonly TimeSpan SparklineWindow = TimeSpan.FromDays(7);
    private const decimal ClosedValueThreshold = 1m;  // value below this ⇒ position effectively exited

    // An asset holding is a stream whose connector binding carries a capital basis (snapshot/asset connector).
    // Value at any instant is the cumulative sum of the (signed) Performance events up to that instant, so the
    // event history *is* the value time-series — 24h / 7d change and the sparkline are derived from it, no extra
    // storage. Series resolution improves as the hourly snapshot poll accumulates points.
    internal static AssetHoldingView? BuildAssetHolding(StreamGrainState s, DateTimeOffset? asOf = null)
    {
        if (s.Binding?.CapitalBasisUsd is not { } capital) return null;
        var now = asOf ?? DateTimeOffset.UtcNow;

        var events = s.Events.OrderBy(e => e.OccurredAt).ToList();
        decimal ValueAt(DateTimeOffset t) => events.Where(e => e.OccurredAt <= t).Sum(e => e.Amount.Amount);

        var valueRaw = ValueAt(now);
        var value = Math.Round(valueRaw, 2);
        var ret = Math.Round(valueRaw - capital, 2);
        var pct = capital != 0 ? Math.Round((valueRaw - capital) / capital, 4) : (decimal?)null;

        var v24 = ValueAt(now - TimeSpan.FromHours(24));
        var change24 = Math.Round(valueRaw - v24, 2);
        var change24Pct = v24 != 0 ? Math.Round((valueRaw - v24) / v24, 4) : (decimal?)null;

        var v7d = ValueAt(now - SparklineWindow);
        var change7d = Math.Round(valueRaw - v7d, 2);
        var change7dPct = v7d != 0 ? Math.Round((valueRaw - v7d) / v7d, 4) : (decimal?)null;

        var sparkline = BuildValueSparkline(events, now, ValueAt);
        var isClosed = Math.Abs(value) < ClosedValueThreshold && capital >= ClosedValueThreshold;

        return new AssetHoldingView(s.Id, s.Name, s.Category, value, Math.Round(capital, 2), ret, pct,
            change24, change24Pct, change7d, change7dPct, sparkline, isClosed);
    }

    // Capital basis at an instant. Uses the recorded history; before the first point capital is 0.
    // With no history (streams created before capital recording existed) we approximate with current capital.
    internal static decimal CapitalAt(ConnectorBindingState binding, DateTimeOffset t)
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

    // Samples the cumulative value at evenly spaced points from the start of the recent window to now.
    // Window starts at the first event (so a freshly tracked holding isn't padded with leading zeros),
    // but never reaches further back than SparklineWindow.
    private static IReadOnlyList<decimal> BuildValueSparkline(
        IReadOnlyList<FlowEventSnapshot> orderedEvents, DateTimeOffset now, Func<DateTimeOffset, decimal> valueAt)
    {
        if (orderedEvents.Count == 0) return [0m];
        var windowStart = now - SparklineWindow;
        var start = orderedEvents[0].OccurredAt > windowStart ? orderedEvents[0].OccurredAt : windowStart;
        if (start >= now) return [Math.Round(valueAt(now), 2)];

        var span = now - start;
        var points = new decimal[SparklinePoints];
        for (var i = 0; i < SparklinePoints; i++)
        {
            var t = start + (span * i / (SparklinePoints - 1));
            points[i] = Math.Round(valueAt(t), 2);
        }
        return points;
    }

    public async Task<IReadOnlyList<AssetHoldingView>> GetAssetHoldingsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var states = await LoadAllAsync(ct);
        return states.Select(s => BuildAssetHolding(s, now)).OfType<AssetHoldingView>()
            .OrderByDescending(h => h.ValueUsd).ToList();
    }

    private static IReadOnlyList<StreamGrainState> ApplyFilter(
        IReadOnlyList<StreamGrainState> states,
        IReadOnlyCollection<Guid>? streamFilter)
    {
        if (streamFilter is null) return states;
        if (streamFilter.Count == 0) return Array.Empty<StreamGrainState>();
        var set = streamFilter as HashSet<Guid> ?? new HashSet<Guid>(streamFilter);
        return states.Where(s => set.Contains(s.Id)).ToList();
    }

    public async Task<WhatIfResultView> GetWhatIfAsync(Scenario scenario, CancellationToken ct)
    {
        var states = await LoadAllAsync(ct);
        var modified = ApplyScenario(states, scenario);

        var baselineHistory = ComputeMonthlyHistory(states, 12);
        var scenarioHistory = ComputeMonthlyHistory(modified, 12);
        var baselineFuture = ProjectFuture(states, 3);
        var scenarioFuture = ProjectFuture(modified, 3);

        var series = new List<ScenarioPointView>(15);
        for (var i = 0; i < baselineHistory.Count; i++)
        {
            series.Add(new ScenarioPointView(
                Label: baselineHistory[i].Label,
                Baseline: baselineHistory[i].Net,
                Scenario: scenarioHistory[i].Net,
                IsProjected: false));
        }
        for (var i = 0; i < baselineFuture.Count; i++)
        {
            series.Add(new ScenarioPointView(
                Label: baselineFuture[i].Label,
                Baseline: baselineFuture[i].Net,
                Scenario: scenarioFuture[i].Net,
                IsProjected: true));
        }

        var baselineTrends = ComputeStreamTrends(states, 12).ToDictionary(t => t.Id);
        var scenarioTrends = ComputeStreamTrends(modified, 12).ToDictionary(t => t.Id);
        var impacts = new List<StreamImpactView>();
        foreach (var (id, b) in baselineTrends)
        {
            var bAvg = b.RecentAverage ?? 0m;
            decimal? sAvg = scenarioTrends.TryGetValue(id, out var s) ? s.RecentAverage : null;
            var sAvgValue = sAvg ?? 0m;
            var sign = b.Direction == Direction.Outcome ? -1m : 1m; // Income and Performance → +1, Outcome → −1
            var delta = (sAvgValue - bAvg) * sign;
            if (Math.Abs(delta) < 0.01m) continue;
            impacts.Add(new StreamImpactView(id, b.Name, b.Direction, bAvg, sAvg, delta));
        }
        impacts = impacts.OrderByDescending(i => Math.Abs(i.Delta)).ToList();

        return new WhatIfResultView(
            BaselineCurrentMonth: ComputeCurrentMonth(states),
            BaselineProjection: ComputeProjection(states),
            ScenarioCurrentMonth: ComputeCurrentMonth(modified),
            ScenarioProjection: ComputeProjection(modified),
            NetSeries: series,
            StreamImpacts: impacts);
    }

    // Recurring cash-flow streams with their calendar + expected-vs-actual (last complete month).
    // Powers the Estable tab's expected-vs-real bullet chart and the recurrence calendar.
    public async Task<IReadOnlyList<EstableStreamView>> GetEstableStreamsAsync(
        CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        var now = DateTimeOffset.UtcNow;
        var thisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var lastStart = thisMonth.AddMonths(-1);    // last complete month

        var rows = new List<EstableStreamView>();
        foreach (var s in states)
        {
            if (s.Direction == Direction.Performance) continue;
            if (s.ExpectedAmount is null && s.Schedule is null) continue;

            var cadence = s.Schedule?.Cadence ?? Cadence.Monthly;
            var anchor = s.Schedule?.Anchor ?? 1;
            var isFixed = s.Schedule?.Variability == Variability.Fixed;
            var perMonth = cadence switch { Cadence.Weekly => 4.345m, Cadence.Biweekly => 2.17m, _ => 1m };
            var expected = Math.Round((s.ExpectedAmount?.Amount ?? 0m) * perMonth, 2);
            var actual = Math.Round(s.Events.Where(e => e.OccurredAt >= lastStart && e.OccurredAt < thisMonth).Sum(e => e.Amount.Amount), 2);

            rows.Add(new EstableStreamView(s.Id, s.Name, s.Category, s.Direction, isFixed, cadence, anchor, expected, actual));
        }
        return rows
            .OrderByDescending(r => r.Direction == Direction.Income)
            .ThenByDescending(r => r.Expected)
            .ToList();
    }

    // Portfolio market value vs invested capital (DCA), monthly. Empty if no asset holdings exist.
    public async Task<PortfolioSeriesView> GetPortfolioSeriesAsync(
        int months, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        var assets = states.Where(s => s.Direction == Direction.Performance && s.Binding?.CapitalBasisUsd is not null).ToList();

        var labels = new List<string>();
        var value = new List<decimal>();
        var capital = new List<decimal>();
        if (assets.Count == 0) return new PortfolioSeriesView(labels, value, capital);

        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = months - 1; i >= 0; i--)
        {
            var mStart = anchor.AddMonths(-i);
            var mEnd = mStart.AddMonths(1).AddTicks(-1);
            labels.Add(mStart.ToString("MMM yy"));
            value.Add(Math.Round(assets.Sum(a => a.Events.Where(e => e.OccurredAt <= mEnd).Sum(e => e.Amount.Amount)), 2));
            capital.Add(Math.Round(assets.Sum(a => CapitalAt(a.Binding!, mEnd)), 2));
        }
        return new PortfolioSeriesView(labels, value, capital);
    }

    // Per-stream monthly series carrying Category + IsFixed, so the funnel dashboard can pivot by
    // category and by predecible/variable on the client without extra round-trips. Cash flow only.
    public async Task<IReadOnlyList<StreamSeriesPointView>> GetStreamSeriesAsync(
        int months, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        return ComputeStreamSeries(states, months);
    }

    private static IReadOnlyList<StreamSeriesPointView> ComputeStreamSeries(IReadOnlyList<StreamGrainState> states, int months)
    {
        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var floor = anchor.AddMonths(-(months - 1));

        var output = new List<StreamSeriesPointView>();
        foreach (var s in states)
        {
            if (s.Direction == Direction.Performance) continue; // funnel: cash flow only
            var isFixed = s.Schedule?.Variability == Variability.Fixed;

            var buckets = new SortedDictionary<(int Y, int M), decimal>();
            for (var i = months - 1; i >= 0; i--)
            {
                var d = anchor.AddMonths(-i);
                buckets[(d.Year, d.Month)] = 0m;
            }
            foreach (var e in s.Events)
            {
                if (e.OccurredAt < floor) continue;
                var key = (e.OccurredAt.Year, e.OccurredAt.Month);
                if (!buckets.ContainsKey(key)) continue;
                buckets[key] += e.Amount.Amount;
            }
            foreach (var (key, amount) in buckets)
                output.Add(new StreamSeriesPointView(
                    key.Y, key.M, s.Id, s.Name, s.Category, s.Direction, isFixed, Math.Round(amount, 2)));
        }
        return output;
    }

    private static IReadOnlyList<MonthlyStreamPointView> ComputeMonthlyHistoryByStream(IReadOnlyList<StreamGrainState> states, int months)
    {
        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var floor = anchor.AddMonths(-(months - 1));

        var output = new List<MonthlyStreamPointView>();
        foreach (var s in states)
        {
            var buckets = new SortedDictionary<(int Y, int M), decimal>();
            for (var i = months - 1; i >= 0; i--)
            {
                var d = anchor.AddMonths(-i);
                buckets[(d.Year, d.Month)] = 0m;
            }

            foreach (var e in s.Events)
            {
                if (e.OccurredAt < floor) continue;
                var key = (e.OccurredAt.Year, e.OccurredAt.Month);
                if (!buckets.ContainsKey(key)) continue;
                buckets[key] += e.Amount.Amount;
            }

            foreach (var (key, amount) in buckets)
            {
                output.Add(new MonthlyStreamPointView(
                    Year: key.Y,
                    Month: key.M,
                    StreamId: s.Id,
                    StreamName: s.Name,
                    Direction: s.Direction,
                    Amount: Math.Round(amount, 2)));
            }
        }
        return output;
    }

    private static IReadOnlyList<YearlyStreamPointView> ComputeYearlyHistoryByStream(IReadOnlyList<StreamGrainState> states)
    {
        if (states.Count == 0) return Array.Empty<YearlyStreamPointView>();

        var allYears = states.SelectMany(s => s.Events.Select(e => e.OccurredAt.Year)).ToList();
        if (allYears.Count == 0) return Array.Empty<YearlyStreamPointView>();
        var minYear = allYears.Min();
        var maxYear = Math.Max(allYears.Max(), DateTimeOffset.UtcNow.Year);

        var output = new List<YearlyStreamPointView>();
        foreach (var s in states)
        {
            var buckets = new SortedDictionary<int, decimal>();
            for (var y = minYear; y <= maxYear; y++) buckets[y] = 0m;

            foreach (var e in s.Events)
                buckets[e.OccurredAt.Year] += e.Amount.Amount;

            foreach (var (year, amount) in buckets)
            {
                output.Add(new YearlyStreamPointView(
                    Year: year,
                    StreamId: s.Id,
                    StreamName: s.Name,
                    Direction: s.Direction,
                    Amount: Math.Round(amount, 2)));
            }
        }
        return output;
    }

    private static IReadOnlyList<YearlyAggregateView> ComputeYearlyHistory(IReadOnlyList<StreamGrainState> states)
    {
        var buckets = new SortedDictionary<int, (decimal Income, decimal Outcome, decimal Performance, int Count, HashSet<int> Months)>();
        foreach (var s in states)
        {
            foreach (var e in s.Events)
            {
                if (!buckets.TryGetValue(e.OccurredAt.Year, out var b))
                    b = (0m, 0m, 0m, 0, new HashSet<int>());
                if (s.Direction == Direction.Income) b.Income += e.Amount.Amount;
                else if (s.Direction == Direction.Outcome) b.Outcome += e.Amount.Amount;
                else if (s.Direction == Direction.Performance) b.Performance += e.Amount.Amount;
                b.Count++;
                b.Months.Add(e.OccurredAt.Month);
                buckets[e.OccurredAt.Year] = b;
            }
        }
        return buckets
            .Select(kv => new YearlyAggregateView(
                Year: kv.Key,
                Income: Math.Round(kv.Value.Income, 2),
                Outcome: Math.Round(kv.Value.Outcome, 2),
                Net: Math.Round(kv.Value.Income - kv.Value.Outcome + kv.Value.Performance, 2),
                EventCount: kv.Value.Count,
                MonthsCovered: kv.Value.Months.Count,
                Performance: Math.Round(kv.Value.Performance, 2)))
            .ToList();
    }

    private static IReadOnlyList<CumulativeBalancePointView> ComputeCumulativeBalance(
        IReadOnlyList<StreamGrainState> states,
        int futureMonths)
    {
        DateTimeOffset? earliest = null;
        foreach (var s in states)
            foreach (var e in s.Events)
                if (earliest is null || e.OccurredAt < earliest) earliest = e.OccurredAt;

        if (earliest is null) return Array.Empty<CumulativeBalancePointView>();

        var now = DateTimeOffset.UtcNow;
        var firstMonth = new DateTimeOffset(earliest.Value.Year, earliest.Value.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var currentMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var historicalMonths = (int)Math.Round((currentMonth - firstMonth).TotalDays / 30.44) + 1;
        if (historicalMonths < 1) historicalMonths = 1;

        var buckets = new SortedDictionary<(int Y, int M), (decimal Income, decimal Outcome, decimal Performance)>();
        for (var i = 0; i < historicalMonths; i++)
        {
            var d = firstMonth.AddMonths(i);
            buckets[(d.Year, d.Month)] = (0m, 0m, 0m);
        }

        foreach (var s in states)
        {
            foreach (var e in s.Events)
            {
                var key = (e.OccurredAt.Year, e.OccurredAt.Month);
                if (!buckets.TryGetValue(key, out var b)) continue;
                if (s.Direction == Direction.Income) b.Income += e.Amount.Amount;
                else if (s.Direction == Direction.Outcome) b.Outcome += e.Amount.Amount;
                else if (s.Direction == Direction.Performance) b.Performance += e.Amount.Amount;
                buckets[key] = b;
            }
        }

        var bucketList = buckets.ToList();

        var completePast = bucketList.Count > 1
            ? bucketList.Take(bucketList.Count - 1).ToArray()
            : Array.Empty<KeyValuePair<(int Y, int M), (decimal Income, decimal Outcome, decimal Performance)>>();
        var avgStableNet = completePast.Length > 0 ? completePast.Average(m => m.Value.Income - m.Value.Outcome) : 0m;
        var avgPerformance = completePast.Length > 0 ? completePast.Average(m => m.Value.Performance) : 0m;
        var avgNet = avgStableNet + avgPerformance;

        var points = new List<CumulativeBalancePointView>(bucketList.Count + futureMonths);
        decimal runningStable = 0m, runningVolatile = 0m;
        foreach (var kv in bucketList)
        {
            runningStable += kv.Value.Income - kv.Value.Outcome;
            runningVolatile += kv.Value.Performance;
            var ts = new DateTimeOffset(kv.Key.Y, kv.Key.M, 1, 0, 0, 0, TimeSpan.Zero);
            var stable = Math.Round(runningStable, 2);
            var volatile_ = Math.Round(runningVolatile, 2);
            points.Add(new CumulativeBalancePointView(
                ts.ToString("MMM yy"), ts, stable + volatile_, IsProjected: false,
                stable, volatile_));
        }

        for (var i = 1; i <= futureMonths; i++)
        {
            runningStable += avgStableNet;
            runningVolatile += avgPerformance;
            var d = currentMonth.AddMonths(i);
            var stable = Math.Round(runningStable, 2);
            var volatile_ = Math.Round(runningVolatile, 2);
            points.Add(new CumulativeBalancePointView(
                d.ToString("MMM yy"), d, stable + volatile_, IsProjected: true,
                stable, volatile_));
        }

        return points;
    }

    private static IReadOnlyList<MonthlyAggregateView> ProjectFuture(IReadOnlyList<StreamGrainState> states, int months)
    {
        var history = ComputeMonthlyHistory(states, 12);
        var complete = history.SkipLast(1).ToArray();
        var avgIncome = complete.Length > 0 ? complete.Average(m => m.Income) : 0m;
        var avgOutcome = complete.Length > 0 ? complete.Average(m => m.Outcome) : 0m;
        var avgPerformance = complete.Length > 0 ? complete.Average(m => m.Performance) : 0m;
        var avgNet = avgIncome - avgOutcome + avgPerformance;

        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var result = new List<MonthlyAggregateView>(months);
        for (var i = 1; i <= months; i++)
        {
            var d = anchor.AddMonths(i);
            result.Add(new MonthlyAggregateView(d.Year, d.Month,
                Math.Round(avgIncome, 2), Math.Round(avgOutcome, 2), Math.Round(avgNet, 2), 0,
                Math.Round(avgPerformance, 2)));
        }
        return result;
    }

    internal static MonthSummaryView ComputeCurrentMonth(IReadOnlyList<StreamGrainState> states)
    {
        var now = DateTimeOffset.UtcNow;
        var startThisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var startPrevMonth = startThisMonth.AddMonths(-1);
        var endThisMonth = startThisMonth.AddMonths(1);

        // Per-direction accumulators are kept because views expose Income/Outcome separately; net = Income - Outcome + Performance.
        decimal income = 0, outcome = 0, performance = 0;
        decimal prevIncomeSamePoint = 0, prevOutcomeSamePoint = 0, prevPerformanceSamePoint = 0;
        var prevSamePoint = startPrevMonth.AddDays((now - startThisMonth).TotalDays);

        foreach (var s in states)
        {
            foreach (var e in s.Events)
            {
                if (e.OccurredAt >= startThisMonth && e.OccurredAt < endThisMonth)
                {
                    if (s.Direction == Direction.Income) income += e.Amount.Amount;
                    else if (s.Direction == Direction.Outcome) outcome += e.Amount.Amount;
                    else if (s.Direction == Direction.Performance) performance += e.Amount.Amount;
                }
                else if (e.OccurredAt >= startPrevMonth && e.OccurredAt < prevSamePoint)
                {
                    if (s.Direction == Direction.Income) prevIncomeSamePoint += e.Amount.Amount;
                    else if (s.Direction == Direction.Outcome) prevOutcomeSamePoint += e.Amount.Amount;
                    else if (s.Direction == Direction.Performance) prevPerformanceSamePoint += e.Amount.Amount;
                }
            }
        }

        var net = income - outcome + performance;
        var prevNet = prevIncomeSamePoint - prevOutcomeSamePoint + prevPerformanceSamePoint;
        var daysIntoMonth = Math.Max(1, (int)Math.Ceiling((now - startThisMonth).TotalDays));
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var onTrack = daysIntoMonth > 0 ? net * daysInMonth / daysIntoMonth : (decimal?)null;

        return new MonthSummaryView(
            Year: now.Year,
            Month: now.Month,
            IncomeMTD: income,
            OutcomeMTD: outcome,
            NetMTD: net,
            OnTrackEom: onTrack,
            PreviousMonthSamePoint: prevNet,
            Delta: net - prevNet,
            DaysIntoMonth: daysIntoMonth,
            DaysInMonth: daysInMonth,
            PerformanceMTD: performance);
    }

    internal static IReadOnlyList<MonthlyAggregateView> ComputeMonthlyHistory(IReadOnlyList<StreamGrainState> states, int months)
    {
        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var buckets = new SortedDictionary<(int Y, int M), (decimal Income, decimal Outcome, decimal Performance, int Count)>();

        for (var i = months - 1; i >= 0; i--)
        {
            var d = anchor.AddMonths(-i);
            buckets[(d.Year, d.Month)] = (0m, 0m, 0m, 0);
        }

        var floor = anchor.AddMonths(-(months - 1));
        // Per-direction accumulators are kept because views expose Income/Outcome separately; net = Income - Outcome + Performance.
        foreach (var s in states)
        {
            foreach (var e in s.Events)
            {
                if (e.OccurredAt < floor) continue;
                var key = (e.OccurredAt.Year, e.OccurredAt.Month);
                if (!buckets.TryGetValue(key, out var bucket)) continue;
                if (s.Direction == Direction.Income) bucket.Income += e.Amount.Amount;
                else if (s.Direction == Direction.Outcome) bucket.Outcome += e.Amount.Amount;
                else if (s.Direction == Direction.Performance) bucket.Performance += e.Amount.Amount;
                bucket.Count++;
                buckets[key] = bucket;
            }
        }

        return buckets
            .Select(kv => new MonthlyAggregateView(kv.Key.Y, kv.Key.M, kv.Value.Income, kv.Value.Outcome,
                                                   kv.Value.Income - kv.Value.Outcome + kv.Value.Performance,
                                                   kv.Value.Count, kv.Value.Performance))
            .ToList();
    }

    internal static IReadOnlyList<StreamTrendView> ComputeStreamTrends(IReadOnlyList<StreamGrainState> states, int sparklineMonths)
    {
        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var trends = new List<StreamTrendView>(states.Count);

        foreach (var s in states.Where(x => x.Status is StreamStatus.Active or StreamStatus.Paused))
        {
            var buckets = new decimal[sparklineMonths];
            for (var i = 0; i < sparklineMonths; i++)
            {
                var bucketStart = anchor.AddMonths(-(sparklineMonths - 1 - i));
                var bucketEnd = bucketStart.AddMonths(1);
                buckets[i] = s.Events
                    .Where(e => e.OccurredAt >= bucketStart && e.OccurredAt < bucketEnd)
                    .Sum(e => e.Amount.Amount);
            }

            decimal? lastMonth = buckets.Length >= 2 ? buckets[^2] : null;

            var nonZero = s.Direction == Direction.Performance
                ? buckets.Where(b => b != 0).ToArray()
                : buckets.Where(b => b > 0).ToArray();
            decimal? avg = nonZero.Length > 0 ? nonZero.Average() : null;

            var includeNegatives = s.Direction == Direction.Performance;
            var (slope, label, detail) = ClassifyTrend(buckets, includeNegatives);

            trends.Add(new StreamTrendView(
                Id: s.Id,
                Name: s.Name,
                Category: s.Category,
                Direction: s.Direction,
                Status: s.Status,
                ExpectedAmount: s.ExpectedAmount?.Amount,
                LastMonthAmount: lastMonth,
                RecentAverage: avg,
                Slope: slope,
                TrendLabel: label,
                TrendDetail: detail,
                Sparkline: buckets));
        }

        return trends
            .OrderByDescending(t => t.Direction == Direction.Income)
            .ThenByDescending(t => t.RecentAverage ?? 0m)
            .ToList();
    }

    internal static ProjectionView ComputeProjection(IReadOnlyList<StreamGrainState> states)
    {
        var monthly = ComputeMonthlyHistory(states, months: 12);
        var current = ComputeCurrentMonth(states);

        if (monthly.Count == 0)
            return new ProjectionView(null, null, null, null, null, "No history yet to project from.");

        var completeMonths = monthly.SkipLast(1).ToArray();
        var avgNet = completeMonths.Length > 0 ? completeMonths.Average(m => m.Net) : 0m;
        var stddev = completeMonths.Length > 1 ? StdDev(completeMonths.Select(m => m.Net).ToArray()) : 0m;

        // Net includes signed Performance, so stddev already reflects volatile streams.
        // Report ~P5–P95 (1.65σ) so volatile assets read as uncertain (wider than a 1σ band).
        const decimal BandSigma = 1.65m;
        var uncertaintyBand = BandSigma * stddev;

        var monthsToYearEnd = Math.Max(0, 12 - DateTimeOffset.UtcNow.Month);

        var eom = current.OnTrackEom;
        var threeMonth = (current.NetMTD == 0 ? avgNet : current.OnTrackEom ?? avgNet) + avgNet * 2;
        var yearEnd = (current.NetMTD == 0 ? avgNet : current.OnTrackEom ?? avgNet) + avgNet * monthsToYearEnd;

        var avgOutcome = completeMonths.Length > 0 ? completeMonths.Average(m => m.Outcome) : 0m;

        int? runway = null;
        string runwayMessage;
        if (avgOutcome <= 0)
        {
            runwayMessage = "No outflows on record yet.";
        }
        else if (avgNet >= 0)
        {
            // avgNet already folds in Performance (signed), so a positive net means no burn.
            runwayMessage = avgNet > 0
                ? "You earn more than you spend — no runway concern."
                : "You break even — no runway concern.";
        }
        else
        {
            // avgNet < 0: net burn per month.
            var burn = -avgNet;
            var assumedSavings = avgNet * Math.Max(completeMonths.Length, 1);
            runway = (int)Math.Max(0, Math.Floor(assumedSavings / burn));
            runwayMessage = $"Spending exceeds income by ~${burn:N0}/month.";
        }

        return new ProjectionView(
            EndOfMonth: Math.Round(eom ?? avgNet, 2),
            ThreeMonthsAhead: Math.Round(threeMonth, 2),
            YearEnd: monthsToYearEnd == 0 ? Math.Round(eom ?? avgNet, 2) : Math.Round(yearEnd, 2),
            Uncertainty: Math.Round(uncertaintyBand, 2),
            RunwayMonths: runway,
            RunwayMessage: runwayMessage);
    }

    private static RetrospectiveView ComputeRetrospective(IReadOnlyList<StreamGrainState> states)
    {
        var now = DateTimeOffset.UtcNow;
        var ytdStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var prevYtdStart = new DateTimeOffset(now.Year - 1, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var prevYearSamePoint = prevYtdStart.AddDays((now - ytdStart).TotalDays);

        decimal ytdIncome = 0, ytdOutcome = 0, ytdPerformance = 0;
        decimal prevYtdIncome = 0, prevYtdOutcome = 0, prevYtdPerformance = 0;
        var monthlyNets = new Dictionary<(int Y, int M), decimal>();
        var hasPrevYearData = false;

        foreach (var s in states)
        {
            foreach (var e in s.Events)
            {
                var signed = SignedNet(s.Direction, e.Amount.Amount);

                if (e.OccurredAt >= ytdStart && e.OccurredAt < now)
                {
                    if (s.Direction == Direction.Income) ytdIncome += e.Amount.Amount;
                    else if (s.Direction == Direction.Outcome) ytdOutcome += e.Amount.Amount;
                    else if (s.Direction == Direction.Performance) ytdPerformance += e.Amount.Amount;
                }
                else if (e.OccurredAt >= prevYtdStart && e.OccurredAt < prevYearSamePoint)
                {
                    hasPrevYearData = true;
                    if (s.Direction == Direction.Income) prevYtdIncome += e.Amount.Amount;
                    else if (s.Direction == Direction.Outcome) prevYtdOutcome += e.Amount.Amount;
                    else if (s.Direction == Direction.Performance) prevYtdPerformance += e.Amount.Amount;
                }

                var key = (e.OccurredAt.Year, e.OccurredAt.Month);
                monthlyNets.TryGetValue(key, out var net);
                monthlyNets[key] = net + signed;
            }
        }

        var ytdNet = ytdIncome - ytdOutcome + ytdPerformance;
        var prevYtdNet = prevYtdIncome - prevYtdOutcome + prevYtdPerformance;

        var completeMonths = monthlyNets
            .Where(kv => !(kv.Key.Y == now.Year && kv.Key.M == now.Month))
            .ToList();

        BestMonthView? best = null, worst = null;
        if (completeMonths.Count > 0)
        {
            var b = completeMonths.MaxBy(kv => kv.Value);
            var w = completeMonths.MinBy(kv => kv.Value);
            best = new BestMonthView(b.Key.Y, b.Key.M, Math.Round(b.Value, 2));
            worst = new BestMonthView(w.Key.Y, w.Key.M, Math.Round(w.Value, 2));
        }

        return new RetrospectiveView(
            YearToDateNet: Math.Round(ytdNet, 2),
            YearToDateIncome: Math.Round(ytdIncome, 2),
            YearToDateOutcome: Math.Round(ytdOutcome, 2),
            PreviousYearSamePointNet: hasPrevYearData ? Math.Round(prevYtdNet, 2) : null,
            YoyDelta: hasPrevYearData ? Math.Round(ytdNet - prevYtdNet, 2) : null,
            BestMonth: best,
            WorstMonth: worst);
    }

    internal static IReadOnlyList<StreamGrainState> ApplyScenario(
        IReadOnlyList<StreamGrainState> states,
        Scenario scenario)
    {
        var result = new List<StreamGrainState>(states.Count);
        foreach (var s in states)
        {
            if (scenario.ExcludedStreamIds.Contains(s.Id)) continue;

            decimal factor = 1m;
            if (scenario.StreamMultipliers.TryGetValue(s.Id, out var sm)) factor *= sm;
            if (scenario.CategoryMultipliers.TryGetValue(s.Category, out var cm)) factor *= cm;
            if (scenario.DirectionMultipliers.TryGetValue(s.Direction, out var dm)) factor *= dm;

            if (factor == 1m) { result.Add(s); continue; }

            result.Add(new StreamGrainState
            {
                Id = s.Id,
                Version = s.Version,
                Name = s.Name,
                Category = s.Category,
                Direction = s.Direction,
                Schedule = s.Schedule,
                ExpectedAmount = s.ExpectedAmount is { } ea ? new MoneyState { Amount = Math.Max(0m, ea.Amount * factor) } : null,
                Status = s.Status,
                Events = s.Events.Select(e => new FlowEventSnapshot
                {
                    Id = e.Id,
                    OccurredAt = e.OccurredAt,
                    Amount = new MoneyState { Amount = s.Direction == Direction.Performance ? e.Amount.Amount * factor : Math.Max(0m, e.Amount.Amount * factor) },
                    Source = e.Source,
                    ExternalRef = e.ExternalRef,
                }).ToList(),
                Binding = s.Binding,
            });
        }
        return result;
    }

    // Net-worth trajectory: savings layer = opening + cumulative(income−outcome) − asset capital(T);
    // assets layer = cumulative Performance value. Projection holds assets flat at the last value with a
    // ±band from historical monthly price-return volatility (capital flows stripped out); savings continues on its recent trend.
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
        var assetMonthlyPct = new List<decimal>();
        decimal prevValue = 0;
        decimal prevCapital = 0;

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

            // Volatility for the projection band is the PRICE return only — strip capital flows
            // (deposits/withdrawals raise value AND capital) so buying more doesn't inflate sigma.
            if (prevValue != 0)
            {
                var priceDelta = (runValue - prevValue) - (capital - prevCapital);
                assetMonthlyPct.Add(priceDelta / prevValue);
            }
            prevValue = runValue;
            prevCapital = capital;
        }

        if (futureMonths > 0 && points.Count > 0)
        {
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
                var band = Math.Round(Math.Abs(flatAssets) * sigmaMonthly * (decimal)Math.Sqrt(i), 2);
                points.Add(new CumulativeBalancePointView(
                    d.ToString("MMM yy"), d, nw, IsProjected: true, savings, flatAssets,
                    BandLow: Math.Round(nw - band, 2), BandHigh: Math.Round(nw + band, 2)));
            }
        }

        return points;
    }

    public async Task<IReadOnlyList<CumulativeBalancePointView>> GetNetWorthTrajectoryAsync(
        int futureMonths, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        var opening = await grains.GetGrain<IOverviewSettingsGrain>(OverviewSettingsGrain.Key)
            .GetOpeningBalanceAsync();
        return ComputeNetWorthTrajectory(states, opening, futureMonths, DateTimeOffset.UtcNow);
    }

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

    public async Task<IReadOnlyList<YearOverYearView>> GetYearOverYearAsync(
        CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null) =>
        ComputeYearOverYear(ApplyFilter(await LoadAllAsync(ct), streamFilter), DateTimeOffset.UtcNow);

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
        EarnSpendGranularity grain, int periods, CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null) =>
        ComputeEarnSpend(ApplyFilter(await LoadAllAsync(ct), streamFilter), grain, periods, DateTimeOffset.UtcNow);

    // "If you'd never spent": cumulative gross income (Income streams only) by month, from the first income to now.
    internal static IReadOnlyList<TrajectoryPointView> ComputeGrossEarned(
        IReadOnlyList<StreamGrainState> states, DateTimeOffset now)
    {
        var income = states.Where(s => s.Direction == Direction.Income)
            .SelectMany(s => s.Events.Select(e => (e.OccurredAt, e.Amount.Amount))).ToList();
        if (income.Count == 0) return Array.Empty<TrajectoryPointView>();

        var first = income.Min(e => e.OccurredAt);
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var points = new List<TrajectoryPointView>();
        decimal cum = 0;
        for (var m = new DateTimeOffset(first.Year, first.Month, 1, 0, 0, 0, TimeSpan.Zero); m <= anchor; m = m.AddMonths(1))
        {
            var mEnd = m.AddMonths(1);
            cum += income.Where(e => e.OccurredAt >= m && e.OccurredAt < mEnd).Sum(e => e.Amount);
            points.Add(new TrajectoryPointView(m, Math.Round(cum, 2)));
        }
        return points;
    }

    // "What you really have": opening balance + cumulative (Income − Outcome), starting at the expense-tracking date
    // (the only period where expenses are complete). Returns empty if no tracking date is set.
    internal static IReadOnlyList<TrajectoryPointView> ComputeRealNetWorth(
        IReadOnlyList<StreamGrainState> states, decimal openingBalance, DateTimeOffset? trackingStart, DateTimeOffset now)
    {
        if (trackingStart is not { } start) return Array.Empty<TrajectoryPointView>();

        var flows = states.Where(s => s.Direction is Direction.Income or Direction.Outcome)
            .SelectMany(s => s.Events.Select(e => (s.Direction, e.OccurredAt, e.Amount.Amount))).ToList();

        var startMonth = new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (startMonth > anchor) return Array.Empty<TrajectoryPointView>();

        var points = new List<TrajectoryPointView>();
        decimal net = openingBalance;
        for (var m = startMonth; m <= anchor; m = m.AddMonths(1))
        {
            var mEnd = m.AddMonths(1);
            foreach (var f in flows.Where(e => e.OccurredAt >= m && e.OccurredAt < mEnd))
                net += f.Direction == Direction.Income ? f.Amount : -f.Amount;
            points.Add(new TrajectoryPointView(m, Math.Round(net, 2)));
        }
        return points;
    }

    // Projected gross earnings and net savings over each horizon, at the current monthly trend.
    // Earnings trend = average monthly income over recent complete months; net trend = average monthly
    // (Income − Outcome) over recent complete months from the expense-tracking date (where expenses are complete).
    internal static IReadOnlyList<EarningsProjectionRowView> ComputeEarningsProjection(
        IReadOnlyList<StreamGrainState> states, DateTimeOffset? trackingStart, DateTimeOffset now)
    {
        var flows = states.Where(s => s.Direction is Direction.Income or Direction.Outcome)
            .SelectMany(s => s.Events.Select(e => (s.Direction, e.OccurredAt, e.Amount.Amount))).ToList();

        // Average over the last 12 complete months (the current, partial month is excluded).
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var windowStart = anchor.AddMonths(-12);
        var trackMonth = trackingStart is { } s ? new DateTimeOffset(s.Year, s.Month, 1, 0, 0, 0, TimeSpan.Zero) : (DateTimeOffset?)null;

        decimal incomeSum = 0, netSum = 0;
        var netMonthCount = 0;
        for (var m = windowStart; m < anchor; m = m.AddMonths(1))
        {
            var mEnd = m.AddMonths(1);
            var inc = flows.Where(f => f.Direction == Direction.Income && f.OccurredAt >= m && f.OccurredAt < mEnd).Sum(f => f.Amount);
            var outc = flows.Where(f => f.Direction == Direction.Outcome && f.OccurredAt >= m && f.OccurredAt < mEnd).Sum(f => f.Amount);
            incomeSum += inc;
            if (trackMonth is null || m >= trackMonth)
            {
                netSum += inc - outc;
                netMonthCount++;
            }
        }

        var avgIncome = incomeSum / 12m;
        var avgNet = netMonthCount > 0 ? netSum / netMonthCount : 0m;

        var horizons = new[] { ("6 months", 6), ("12 months", 12), ("3 years", 36), ("5 years", 60) };
        return horizons.Select(h => new EarningsProjectionRowView(
            h.Item1, h.Item2, Math.Round(avgIncome * h.Item2, 2), Math.Round(avgNet * h.Item2, 2))).ToList();
    }

    public async Task<IReadOnlyList<TrajectoryPointView>> GetGrossEarnedAsync(
        CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null) =>
        ComputeGrossEarned(ApplyFilter(await LoadAllAsync(ct), streamFilter), DateTimeOffset.UtcNow);

    public async Task<IReadOnlyList<TrajectoryPointView>> GetRealNetWorthAsync(
        CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        var settings = grains.GetGrain<Grains.IOverviewSettingsGrain>(Grains.OverviewSettingsGrain.Key);
        var opening = await settings.GetOpeningBalanceAsync();
        var trackingStart = await settings.GetExpenseTrackingStartAsync();
        return ComputeRealNetWorth(states, opening, trackingStart, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<EarningsProjectionRowView>> GetEarningsProjectionAsync(
        CancellationToken ct, IReadOnlyCollection<Guid>? streamFilter = null)
    {
        var states = ApplyFilter(await LoadAllAsync(ct), streamFilter);
        var trackingStart = await grains.GetGrain<Grains.IOverviewSettingsGrain>(Grains.OverviewSettingsGrain.Key)
            .GetExpenseTrackingStartAsync();
        return ComputeEarningsProjection(states, trackingStart, DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<StreamGrainState>> LoadAllAsync(CancellationToken ct)
    {
        var index = grains.GetGrain<IStreamIndexGrain>(StreamIndexGrain.SingletonKey);
        var ids = await index.GetAllAsync();
        var states = new List<StreamGrainState>(ids.Count);
        foreach (var id in ids)
        {
            var state = await grains.GetGrain<IStreamGrain>(id).GetAsync();
            if (state.Status is StreamStatus.Stopped or StreamStatus.Deleted) continue;
            states.Add(state);
        }
        return states;
    }

    private static (decimal? Slope, string Label, string Detail) ClassifyTrend(decimal[] series, bool includeNegatives = false)
    {
        var nonZero = includeNegatives
            ? series.Where(s => s != 0).ToArray()
            : series.Where(s => s > 0).ToArray();
        if (nonZero.Length < 3) return (null, "Insufficient data", "");

        var n = nonZero.Length;
        var xs = Enumerable.Range(0, n).Select(i => (decimal)i).ToArray();
        var meanX = xs.Average();
        var meanY = nonZero.Average();
        var num = xs.Zip(nonZero, (x, y) => (x - meanX) * (y - meanY)).Sum();
        var den = xs.Select(x => (x - meanX) * (x - meanX)).Sum();
        var slope = den == 0 ? 0m : num / den;

        var stddev = StdDev(nonZero);
        // For Performance with mixed signs, meanY may be near zero or negative — use Math.Abs for CV denominator.
        var absMeanY = Math.Abs(meanY);
        var cv = absMeanY > 0 ? stddev / absMeanY : 0m;
        if (cv > VolatilityThreshold) return (slope, "Volatile", "varies a lot");

        // Steady/trending comparisons: use absMeanY to avoid nonsense when meanY <= 0.
        if (absMeanY <= 0) return (slope, "Steady", "consistent month over month");
        var threshold = absMeanY * SteadyThresholdPctOfAvg;
        if (Math.Abs(slope) < threshold) return (slope, "Steady", "consistent month over month");

        var sign = slope > 0 ? "Trending up" : "Trending down";
        var monthly = Math.Round(Math.Abs(slope), 0);
        return (slope, sign, $"{(slope > 0 ? "+" : "−")}${monthly:N0}/month");
    }

    private static decimal StdDev(decimal[] values)
    {
        if (values.Length < 2) return 0m;
        var mean = values.Average();
        var variance = values.Select(v => (v - mean) * (v - mean)).Sum() / (values.Length - 1);
        return (decimal)Math.Sqrt((double)variance);
    }
}
