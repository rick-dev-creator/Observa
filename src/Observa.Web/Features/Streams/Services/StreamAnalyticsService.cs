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

        var monthlyNets = buckets.Select(kv => (Key: kv.Key, Net: kv.Value.Income - kv.Value.Outcome + kv.Value.Performance)).ToList();

        var completePast = monthlyNets.Count > 1
            ? monthlyNets.Take(monthlyNets.Count - 1).Select(m => m.Net).ToArray()
            : Array.Empty<decimal>();
        var avgNet = completePast.Length > 0 ? completePast.Average() : 0m;

        var points = new List<CumulativeBalancePointView>(monthlyNets.Count + futureMonths);
        decimal running = 0m;
        foreach (var (key, net) in monthlyNets)
        {
            running += net;
            var ts = new DateTimeOffset(key.Y, key.M, 1, 0, 0, 0, TimeSpan.Zero);
            points.Add(new CumulativeBalancePointView(ts.ToString("MMM yy"), ts, Math.Round(running, 2), IsProjected: false));
        }

        for (var i = 1; i <= futureMonths; i++)
        {
            running += avgNet;
            var d = currentMonth.AddMonths(i);
            points.Add(new CumulativeBalancePointView(d.ToString("MMM yy"), d, Math.Round(running, 2), IsProjected: true));
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

    private static IReadOnlyList<StreamTrendView> ComputeStreamTrends(IReadOnlyList<StreamGrainState> states, int sparklineMonths)
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

            var nonZero = buckets.Where(b => b > 0).ToArray();
            decimal? avg = nonZero.Length > 0 ? nonZero.Average() : null;

            var (slope, label, detail) = ClassifyTrend(buckets);

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

        var monthsToYearEnd = Math.Max(0, 12 - DateTimeOffset.UtcNow.Month);

        var eom = current.OnTrackEom;
        var threeMonth = (current.NetMTD == 0 ? avgNet : current.OnTrackEom ?? avgNet) + avgNet * 2;
        var yearEnd = (current.NetMTD == 0 ? avgNet : current.OnTrackEom ?? avgNet) + avgNet * monthsToYearEnd;

        var avgOutcome = completeMonths.Length > 0 ? completeMonths.Average(m => m.Outcome) : 0m;
        var avgIncome = completeMonths.Length > 0 ? completeMonths.Average(m => m.Income) : 0m;

        int? runway = null;
        string runwayMessage;
        if (avgOutcome <= 0)
        {
            runwayMessage = "No outflows on record yet.";
        }
        else if (avgIncome >= avgOutcome)
        {
            runwayMessage = avgIncome > avgOutcome
                ? "You earn more than you spend — no runway concern."
                : "You break even — no runway concern.";
        }
        else
        {
            var burn = avgOutcome - avgIncome;
            var assumedSavings = avgNet * Math.Max(completeMonths.Length, 1);
            runway = (int)Math.Max(0, Math.Floor(assumedSavings / burn));
            runwayMessage = $"Spending exceeds income by ~${burn:N0}/month.";
        }

        return new ProjectionView(
            EndOfMonth: Math.Round(eom ?? avgNet, 2),
            ThreeMonthsAhead: Math.Round(threeMonth, 2),
            YearEnd: monthsToYearEnd == 0 ? Math.Round(eom ?? avgNet, 2) : Math.Round(yearEnd, 2),
            Uncertainty: Math.Round(stddev, 2),
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

    private static IReadOnlyList<StreamGrainState> ApplyScenario(
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
                    Amount = new MoneyState { Amount = Math.Max(0m, e.Amount.Amount * factor) },
                    Source = e.Source,
                    ExternalRef = e.ExternalRef,
                }).ToList(),
                Binding = s.Binding,
            });
        }
        return result;
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

    private static (decimal? Slope, string Label, string Detail) ClassifyTrend(decimal[] series)
    {
        var nonZero = series.Where(s => s > 0).ToArray();
        if (nonZero.Length < 3) return (null, "Insufficient data", "");

        var n = nonZero.Length;
        var xs = Enumerable.Range(0, n).Select(i => (decimal)i).ToArray();
        var meanX = xs.Average();
        var meanY = nonZero.Average();
        var num = xs.Zip(nonZero, (x, y) => (x - meanX) * (y - meanY)).Sum();
        var den = xs.Select(x => (x - meanX) * (x - meanX)).Sum();
        var slope = den == 0 ? 0m : num / den;

        var stddev = StdDev(nonZero);
        var cv = meanY > 0 ? stddev / meanY : 0m;
        if (cv > VolatilityThreshold) return (slope, "Volatile", "varies a lot");

        var threshold = meanY * SteadyThresholdPctOfAvg;
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
