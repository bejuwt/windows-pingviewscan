using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PingViewScan.Core;

/// <summary>One ICMP echo attempt against a host.</summary>
public sealed record PingSample(int Sequence, DateTimeOffset Timestamp, double? RttMs)
{
    public bool IsLost => RttMs is null;
}

/// <summary>Aggregate counters for a host for the current session.</summary>
public sealed class HostStats : INotifyPropertyChanged
{
    private int _sent;
    private int _received;
    private double? _last;
    private double? _min;
    private double? _max;
    private double _total;

    public int Sent => _sent;
    public int Received => _received;
    public int Lost => _sent - _received;
    public double LossPercent => _sent > 0 ? (double)Lost / _sent * 100.0 : 0.0;
    public double? Last => _last;
    public double? Min => _min;
    public double? Max => _max;
    public double? Average => _received > 0 ? _total / _received : null;

    public void Add(double? rtt)
    {
        _sent++;
        if (rtt is { } ms)
        {
            _received++;
            _last = ms;
            _total += ms;
            _min = _min is { } mn ? Math.Min(mn, ms) : ms;
            _max = _max is { } mx ? Math.Max(mx, ms) : ms;
        }
        else
        {
            _last = null;
        }
        RaiseAll();
    }

    public void Reset()
    {
        _sent = 0;
        _received = 0;
        _last = null;
        _min = null;
        _max = null;
        _total = 0;
        RaiseAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(Sent), nameof(Received), nameof(Lost), nameof(LossPercent),
                     nameof(Last), nameof(Min), nameof(Max), nameof(Average)
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

/// <summary>A monitored host shown in the table.</summary>
public sealed class Host : INotifyPropertyChanged
{
    private string? _ip;
    private string? _hostname;
    private bool _enabled = true;

    public Host(string address)
    {
        Address = address;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Address { get; }

    public string? Ip
    {
        get => _ip;
        set => Set(ref _ip, value);
    }

    public string? Hostname
    {
        get => _hostname;
        set => Set(ref _hostname, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public HostStats Stats { get; } = new();
    public List<PingSample> Samples { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed record HostReport(string Address, string? Ip, HostStats Stats, IReadOnlyList<PingSample> Samples);

public sealed record SessionReport(
    DateTimeOffset Generated,
    DateTimeOffset? Started,
    DateTimeOffset? Ended,
    double Interval,
    double Timeout,
    IReadOnlyList<HostReport> Hosts);
