using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services.Views;

namespace Observa.Features.Streams.Services;

public sealed class StreamAnalyticsService(IGrainFactory grains)
{
    private const decimal VolatilityThreshold = 0.30m; // stddev/mean > 30% → Volatile
    private const decimal SteadyThresholdPctOfAvg = 0.05m;

    public async Task<MonthSummaryView> GetCurrentMonthAsync(CancellationToken ct)
    {
        var states = await LoadAllAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var startThisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var startPrevMonth = startThisMonth.AddMonths(-1);
        var endThisMonth = startThisMonth.AddMonths(1);

        decimal income = 0, outcome = 0;
        decimal prevIncomeSamePoint = 0, prevOutcomeSamePoint = 0;
        var prevSamePoint = startPrevMonth.AddDays((now - startThisMonth).TotalDays);

        foreach (var s in states)
        {
            foreach (var e in s.Events)
            {
                if (e.OccurredAt >= startThisMonth && e.OccurredAt < endThisMonth)
                {
                    if (s.Direction == Direction.Income) income += e.Amount.Amount;
                    else outcome += e.Amount.Amount;
                }
                else if (e.OccurredAt >= startPrevMonth && e.OccurredAt < prevSamePoint)
                {
                    if (s.Direction == Direction.Income) prevIncomeSamePoint += e.Amount.Amount;
                    else prevOutcomeSamePoint += e.Amount.Amount;
                }
            }
        }

        var net = income - outcome;
        var prevNet = prevIncomeSamePoint - prevOutcomeSamePoint;
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
            DaysInMonth: daysInMonth);
    }

    public async Task<IReadOnlyList<MonthlyAggregateView>> GetMonthlyHistoryAsync(int months, CancellationToken ct)
    {
        var states = await LoadAllAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var buckets = new SortedDictionary<(int Y, int M), (decimal Income, decimal Outcome, int Count)>();

        for (var i = months - 1; i >= 0; i--)
        {
            var d = anchor.AddMonths(-i);
            buckets[(d.Year, d.Month)] = (0m, 0m, 0);
        }

        var floor = anchor.AddMonths(-(months - 1));
        foreach (var s in states)
        {
            foreach (var e in s.Events)
            {
                if (e.OccurredAt < floor) continue;
                var key = (e.OccurredAt.Year, e.OccurredAt.Month);
                if (!buckets.TryGetValue(key, out var bucket)) continue;
                if (s.Direction == Direction.Income) bucket.Income += e.Amount.Amount;
                else bucket.Outcome += e.Amount.Amount;
                bucket.Count++;
                buckets[key] = bucket;
            }
        }

        return buckets
            .Select(kv => new MonthlyAggregateView(kv.Key.Y, kv.Key.M, kv.Value.Income, kv.Value.Outcome,
                                                   kv.Value.Income - kv.Value.Outcome, kv.Value.Count))
            .ToList();
    }

    public async Task<IReadOnlyList<StreamTrendView>> GetStreamTrendsAsync(int sparklineMonths, CancellationToken ct)
    {
        var states = await LoadAllAsync(ct);
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

    public async Task<ProjectionView> GetProjectionAsync(CancellationToken ct)
    {
        var monthly = await GetMonthlyHistoryAsync(months: 12, ct);
        var states = await LoadAllAsync(ct);
        var current = await GetCurrentMonthAsync(ct);

        if (monthly.Count == 0)
            return new ProjectionView(null, null, null, null, null, "No history yet to project from.");

        var completeMonths = monthly.SkipLast(1).ToArray(); // exclude current incomplete month
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
            var burn = avgOutcome - avgIncome;          // ≤ 0
            runwayMessage = avgIncome > avgOutcome
                ? "You earn more than you spend — no runway concern."
                : "You break even — no runway concern.";
            _ = burn;
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

    private async Task<IReadOnlyList<StreamGrainState>> LoadAllAsync(CancellationToken ct)
    {
        var index = grains.GetGrain<IStreamIndexGrain>(StreamIndexGrain.SingletonKey);
        var ids = await index.GetAllAsync();
        var states = new List<StreamGrainState>(ids.Count);
        foreach (var id in ids)
        {
            var state = await grains.GetGrain<IStreamGrain>(id).GetAsync();
            if (state.Status is StreamStatus.Deleted) continue;
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
