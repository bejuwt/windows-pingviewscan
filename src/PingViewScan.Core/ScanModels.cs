using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PingViewScan.Core;

/// <summary>One address probed by the network scanner.</summary>
public sealed class ScanResult : INotifyPropertyChanged
{
    private string? _hostname;
    private double? _pingMs;
    private List<ushort> _openPorts = new();
    private bool _scanned;

    public ScanResult(string ip)
    {
        Ip = ip;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Ip { get; }

    public string? Hostname
    {
        get => _hostname;
        set => Set(ref _hostname, value);
    }

    public double? PingMs
    {
        get => _pingMs;
        set
        {
            if (Set(ref _pingMs, value))
            {
                OnPropertyChanged(nameof(IsAlive));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public List<ushort> OpenPorts
    {
        get => _openPorts;
        set
        {
            if (Set(ref _openPorts, value))
            {
                OnPropertyChanged(nameof(IsAlive));
                OnPropertyChanged(nameof(OpenPortsText));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool Scanned
    {
        get => _scanned;
        set
        {
            if (Set(ref _scanned, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsAlive => PingMs is not null || OpenPorts.Count > 0;

    public string StatusText => IsAlive ? "up" : Scanned ? "no response" : "—";

    public string OpenPortsText =>
        OpenPorts.Count == 0 ? "—" : string.Join(", ", OpenPorts.Select(p => p.ToString(CultureInfo.InvariantCulture)));

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Grouped port lists offered in the scanner UI.</summary>
public sealed record PortPreset(string Name, IReadOnlyList<ushort> Ports)
{
    public string Text => string.Join(",", Ports.Select(p => p.ToString(CultureInfo.InvariantCulture)));

    public static readonly IReadOnlyList<PortPreset> All = new[]
    {
        new PortPreset("Common services",
            new ushort[]
            {
                21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 554, 587, 993, 995, 1433, 1723,
                3306, 3389, 5900, 5985, 5986, 8080, 8443, 9100
            }),
        new PortPreset("Web servers", new ushort[] { 80, 443, 3000, 5000, 8000, 8080, 8443, 8888, 9000 }),
        new PortPreset("Windows / AD", new ushort[] { 53, 88, 135, 139, 389, 445, 464, 636, 3268, 3389, 5985, 5986, 5357 }),
        new PortPreset("Infrastructure", new ushort[] { 22, 23, 53, 67, 80, 123, 161, 179, 443, 514, 1723, 5060 }),
        new PortPreset("Remote access", new ushort[] { 22, 23, 3389, 5900, 5901, 5985, 5986 })
    };
}

/// <summary>Parses comma-separated ports and ranges, e.g. "22,80,443,8000-8010".</summary>
public static class PortList
{
    public static List<ushort> Parse(string text)
    {
        var set = new SortedSet<ushort>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return set.ToList();
        }

        foreach (var token in text.Split(new[] { ',', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = token.IndexOf('-');
            if (dash > 0)
            {
                if (ushort.TryParse(token[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) &&
                    ushort.TryParse(token[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end) &&
                    start <= end)
                {
                    for (var port = (int)start; port <= end; port++)
                    {
                        set.Add((ushort)port);
                    }
                }
            }
            else if (ushort.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                set.Add(value);
            }
        }
        return set.ToList();
    }
}
