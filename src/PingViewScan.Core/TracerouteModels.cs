using System.Globalization;

namespace PingViewScan.Core;

/// <summary>One TTL "ring" of a traceroute: the router(s) that answered for this hop.</summary>
public sealed class TracerouteHop
{
    public TracerouteHop(int ttl)
    {
        Ttl = ttl;
    }

    public int Ttl { get; }
    public string? Host { get; set; }
    public string? Ip { get; set; }

    /// <summary>Successful probe round-trip times, in milliseconds.</summary>
    public List<double> Rtts { get; } = new();

    /// <summary>Probes sent to this hop (responded + timed out).</summary>
    public int Sent { get; set; }

    /// <summary>Probes that received no answer.</summary>
    public int Timeouts { get; set; }

    public bool IsTimeout => Rtts.Count == 0;
    public int Received => Rtts.Count;
    public double? MinRtt => Rtts.Count > 0 ? Rtts.Min() : null;
    public double? MaxRtt => Rtts.Count > 0 ? Rtts.Max() : null;
    public double? LastRtt => Rtts.Count > 0 ? Rtts[^1] : null;
    public double? AvgRtt => Rtts.Count > 0 ? Rtts.Sum() / Rtts.Count : null;

    public double LossPercent => Sent > 0
        ? (double)Timeouts / Sent * 100.0
        : (IsTimeout ? 100.0 : 0.0);

    /// <summary>Best available label for the hop.</summary>
    public string DisplayHost
    {
        get
        {
            if (!string.IsNullOrEmpty(Host) && Host != Ip)
            {
                return Host;
            }
            return Ip ?? "*";
        }
    }

    public LatencyLevel LatencyLevel => LatencyLevels.FromRtt(MinRtt);

    public string LatencyHex => LatencyLevel.Hex();

    public string SentRecvLost => $"{Sent} / {Received} / {Timeouts}";

    public string LossText => LossPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string LastMinMax =>
        $"{Format(LastRtt)} / {Format(MinRtt)} / {Format(MaxRtt)}";

    public string AvgText => Format(AvgRtt);

    private static string Format(double? value) =>
        value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "—";
}

/// <summary>
/// Per-hop latency aggregated across one or more traceroute passes. In
/// continuous mode a new <see cref="TracerouteHop"/> is merged in on every pass
/// so the min/avg/max reflect the whole session rather than a single trace.
/// </summary>
public sealed class HopLatencyStats
{
    public HopLatencyStats(int ttl)
    {
        Ttl = ttl;
    }

    public HopLatencyStats(TracerouteHop hop) : this(hop.Ttl)
    {
        Merge(hop);
    }

    public int Ttl { get; }
    public string? Host { get; private set; }
    public string? Ip { get; private set; }

    /// <summary>Probes that received a response (across all passes).</summary>
    public int SampleCount { get; private set; }

    public double SumRtt { get; private set; }
    public double? MinRtt { get; private set; }
    public double? MaxRtt { get; private set; }

    /// <summary>Most recent successful probe round-trip time.</summary>
    public double? LastRtt { get; private set; }

    /// <summary>Probes sent to this hop (responded + timed out) across all passes.</summary>
    public int Sent { get; private set; }

    public int Timeouts { get; private set; }

    public int Received => SampleCount;
    public double? AvgRtt => SampleCount > 0 ? SumRtt / SampleCount : null;

    public double LossPercent => Sent > 0
        ? (double)Timeouts / Sent * 100.0
        : (SampleCount == 0 ? 100.0 : 0.0);

    public string DisplayHost
    {
        get
        {
            if (!string.IsNullOrEmpty(Host) && Host != Ip)
            {
                return Host;
            }
            return Ip ?? "*";
        }
    }

    public LatencyLevel LatencyLevel => LatencyLevels.FromRtt(MinRtt);

    public string LatencyHex => LatencyLevel.Hex();

    public string SentRecvLost => $"{Sent} / {Received} / {Timeouts}";

    public string LossText => LossPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string LastMinMax =>
        $"{Format(LastRtt)} / {Format(MinRtt)} / {Format(MaxRtt)}";

    public string AvgText => Format(AvgRtt);

    /// <summary>Fold one pass's hop results into the running aggregate.</summary>
    public void Merge(TracerouteHop hop)
    {
        if (!string.IsNullOrEmpty(hop.Host))
        {
            Host = hop.Host;
        }
        if (!string.IsNullOrEmpty(hop.Ip))
        {
            Ip = hop.Ip;
        }

        foreach (var rtt in hop.Rtts)
        {
            SampleCount++;
            SumRtt += rtt;
            MinRtt = MinRtt is { } min ? Math.Min(min, rtt) : rtt;
            MaxRtt = MaxRtt is { } max ? Math.Max(max, rtt) : rtt;
        }

        if (hop.Rtts.Count > 0)
        {
            LastRtt = hop.Rtts[^1];
        }

        Sent += hop.Sent;
        Timeouts += hop.Timeouts;
    }

    private static string Format(double? value) =>
        value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "—";
}

/// <summary>Speed buckets shared between the on-screen path and the HTML report.</summary>
public enum LatencyLevel
{
    TimedOut,
    Fast,
    Moderate,
    Slow,
    VerySlow
}

public static class LatencyLevels
{
    public static LatencyLevel FromRtt(double? rtt)
    {
        if (rtt is not { } value)
        {
            return LatencyLevel.TimedOut;
        }
        return value switch
        {
            < 30 => LatencyLevel.Fast,
            < 80 => LatencyLevel.Moderate,
            < 150 => LatencyLevel.Slow,
            _ => LatencyLevel.VerySlow
        };
    }

    public static string Hex(this LatencyLevel level) => level switch
    {
        LatencyLevel.TimedOut => "#cbd5e1",
        LatencyLevel.Fast => "#16a34a",
        LatencyLevel.Moderate => "#65a30d",
        LatencyLevel.Slow => "#d97706",
        LatencyLevel.VerySlow => "#dc2626",
        _ => "#cbd5e1"
    };

    public static string Label(this LatencyLevel level) => level switch
    {
        LatencyLevel.TimedOut => "no response",
        LatencyLevel.Fast => "< 30 ms",
        LatencyLevel.Moderate => "30–80 ms",
        LatencyLevel.Slow => "80–150 ms",
        LatencyLevel.VerySlow => "≥ 150 ms",
        _ => "no response"
    };
}

/// <summary>Snapshot of a traceroute run, used by the report exporter and CSV export.</summary>
public sealed record TracerouteReport(
    string Target,
    string? ResolvedIP,
    DateTimeOffset Generated,
    DateTimeOffset? Started,
    DateTimeOffset? Finished,
    int MaxHops,
    int Queries,
    double Timeout,
    IReadOnlyList<TracerouteHop> Hops,
    IReadOnlyList<HopLatencyStats> Stats,
    int Rounds)
{
    public IReadOnlyList<TracerouteHop> RespondedHops => Hops.Where(h => !h.IsTimeout).ToList();

    public int TimeoutCount => Hops.Sum(h => h.Timeouts);

    public int ProbeCount => Hops.Sum(h => h.Sent);

    public TimeSpan Duration => (Finished ?? DateTimeOffset.Now) - (Started ?? Generated);

    /// <summary>Aggregated stats if present, otherwise a one-pass view of the hops.</summary>
    public IReadOnlyList<HopLatencyStats> StatsOrHops =>
        Stats.Count > 0 ? Stats : Hops.Select(h => new HopLatencyStats(h)).ToList();

    public bool ReachedTarget =>
        ResolvedIP is { } ip && Hops.Any(h => h.Ip == ip && !h.IsTimeout);
}
