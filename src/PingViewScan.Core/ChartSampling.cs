namespace PingViewScan.Core;

/// <summary>Which live graph to draw.</summary>
public enum ChartMode
{
    Latency,
    Loss
}

/// <summary>Which hosts to draw in the live graph.</summary>
public enum HostFilterMode
{
    All,
    Selected
}

/// <summary>Selectable time window for the live session graphs.</summary>
public sealed record ChartTimeframe(string Label, double? Seconds)
{
    public static ChartTimeframe FiveMinutes { get; } = new("5 min", 5 * 60);
    public static ChartTimeframe TenMinutes { get; } = new("10 min", 10 * 60);
    public static ChartTimeframe ThirtyMinutes { get; } = new("30 min", 30 * 60);
    public static ChartTimeframe SixtyMinutes { get; } = new("60 min", 60 * 60);
    public static ChartTimeframe All { get; } = new("All", null);

    public static IReadOnlyList<ChartTimeframe> AllOptions { get; } = new[]
    {
        FiveMinutes, TenMinutes, ThirtyMinutes, SixtyMinutes, All
    };

    /// <summary>Earliest sample time to display, given the most recent sample time.</summary>
    public DateTimeOffset? Cutoff(DateTimeOffset latest) =>
        Seconds is { } seconds ? latest.AddSeconds(-seconds) : null;
}

/// <summary>
/// Shared decimation helpers for the on-screen charts.
///
/// Long sessions are decimated so the whole window stays visible without
/// overwhelming the renderer, and the per-series limit shrinks as the host
/// count grows so the total point count stays bounded. Decimation is aligned to
/// absolute time buckets so the grid never moves as the window slides — that
/// keeps the lines stable instead of visibly flickering on every refresh.
/// </summary>
public static class ChartSampling
{
    public const int MaxPointsPerSeries = 1500;
    public const int TotalPointBudget = 3000;
    public const double MinBucketSeconds = 0.5;

    public static int PerSeriesLimit(int hostCount) =>
        Math.Max(100, Math.Min(MaxPointsPerSeries, TotalPointBudget / Math.Max(1, hostCount)));

    /// <summary>Width of the time buckets used to decimate one series.</summary>
    public static double BucketWidth(ChartTimeframe timeframe, double span, int limit)
    {
        var target = Math.Max(MinBucketSeconds, (timeframe.Seconds ?? span) / Math.Max(1, limit));
        return timeframe.Seconds is null ? LadderWidth(target) : target;
    }

    /// <summary>Smallest "nice" bucket width that is at least <paramref name="target"/>.</summary>
    public static double LadderWidth(double target)
    {
        var ladder = new double[]
        {
            1, 2, 5, 10, 20, 30, 60, 120, 300, 600,
            1800, 3600, 7200, 21600, 43200, 86400
        };
        foreach (var width in ladder)
        {
            if (width >= target)
            {
                return width;
            }
        }
        return ladder[^1];
    }

    /// <summary>
    /// Decimates a time-ordered series to at most roughly <paramref name="limit"/>
    /// points by keeping the newest item in each fixed-width, absolutely-aligned
    /// time bucket.
    /// </summary>
    public static IReadOnlyList<T> Decimate<T>(
        IReadOnlyList<T> items,
        Func<T, DateTimeOffset> time,
        double bucketSeconds,
        int limit)
    {
        if (items.Count <= limit || bucketSeconds <= 0)
        {
            return items;
        }

        var result = new List<T>(limit + 2);
        var currentBucket = BucketOf(time(items[0]), bucketSeconds);
        var newestInBucket = items[0];

        for (var index = 1; index < items.Count; index++)
        {
            var bucket = BucketOf(time(items[index]), bucketSeconds);
            if (bucket == currentBucket)
            {
                newestInBucket = items[index];
            }
            else
            {
                result.Add(newestInBucket);
                currentBucket = bucket;
                newestInBucket = items[index];
            }
        }

        result.Add(newestInBucket);
        return result;
    }

    /// <summary>Windows a host's full history down to the samples inside the timeframe.</summary>
    public static IReadOnlyList<PingSample> VisibleSamples(
        IReadOnlyList<PingSample> samples,
        ChartTimeframe timeframe,
        DateTimeOffset latest,
        int limit)
    {
        if (samples.Count == 0)
        {
            return Array.Empty<PingSample>();
        }

        var startIndex = 0;
        if (timeframe.Cutoff(latest) is { } cutoff)
        {
            // Samples are appended in time order, so binary-search the first one in range.
            var low = 0;
            var high = samples.Count;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (samples[mid].Timestamp < cutoff)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }
            startIndex = low;
        }

        if (startIndex >= samples.Count)
        {
            return Array.Empty<PingSample>();
        }

        var scoped = new List<PingSample>(samples.Count - startIndex);
        for (var index = startIndex; index < samples.Count; index++)
        {
            scoped.Add(samples[index]);
        }

        var span = (scoped[^1].Timestamp - scoped[0].Timestamp).TotalSeconds;
        var width = BucketWidth(timeframe, span, limit);
        return Decimate(scoped, s => s.Timestamp, width, limit);
    }

    private static long BucketOf(DateTimeOffset time, double bucketSeconds) =>
        (long)Math.Floor(time.ToUniversalTime().Ticks / (bucketSeconds * TimeSpan.TicksPerSecond));
}
