using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PingViewScan.Core;

namespace PingViewScan.App.ViewModels;

public sealed class ScanViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private DateTimeOffset? _started;
    private DateTimeOffset? _finished;
    private bool _isRunning;
    private string _targets = "10.0.0.0/24";
    private string _portsText = "22,80,443";
    private PortPreset? _selectedPreset;
    private double _pingTimeout = 1;
    private double _portTimeout = 1;
    private int _concurrency = 64;
    private string _statusText = "Idle";

    public ScanViewModel()
    {
        Presets = new ObservableCollection<PortPreset>(PortPreset.All);
        StartCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsRunning);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning);
        ExportHtmlCommand = new RelayCommand(_ => ExportHtml(), _ => Results.Count > 0);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => Results.Count > 0);
    }

    public ObservableCollection<PortPreset> Presets { get; }
    public ObservableCollection<ScanResult> Results { get; } = new();

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ExportHtmlCommand { get; }
    public RelayCommand ExportCsvCommand { get; }

    public string Targets
    {
        get => _targets;
        set => Set(ref _targets, value);
    }

    public string PortsText
    {
        get => _portsText;
        set => Set(ref _portsText, value);
    }

    public PortPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (Set(ref _selectedPreset, value) && value is not null)
            {
                PortsText = value.Text;
            }
        }
    }

    public double PingTimeout
    {
        get => _pingTimeout;
        set => Set(ref _pingTimeout, value);
    }

    public double PortTimeout
    {
        get => _portTimeout;
        set => Set(ref _portTimeout, value);
    }

    public int Concurrency
    {
        get => _concurrency;
        set => Set(ref _concurrency, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    private async Task StartAsync()
    {
        Results.Clear();
        var range = IPRange.Expand(Targets);
        var rows = new Dictionary<string, ScanResult>();
        foreach (var ip in range)
        {
            if (rows.ContainsKey(ip))
            {
                continue;
            }
            var row = new ScanResult(ip);
            rows[ip] = row;
            Results.Add(row);
        }

        if (Results.Count == 0)
        {
            StatusText = "No valid target addresses.";
            return;
        }

        var ports = PortList.Parse(PortsText);
        var options = new ScanOptions(Targets, ports, PingTimeout, PortTimeout, Math.Max(1, Concurrency));

        _cts = new CancellationTokenSource();
        _started = DateTimeOffset.Now;
        _finished = null;
        IsRunning = true;
        StatusText = $"Scanning {Results.Count} address(es)…";

        var progress = new Progress<ScanResult>(completed =>
        {
            if (rows.TryGetValue(completed.Ip, out var row))
            {
                row.PingMs = completed.PingMs;
                row.OpenPorts = completed.OpenPorts;
                row.Hostname = completed.Hostname;
                row.Scanned = true;
            }
            var done = rows.Values.Count(r => r.Scanned);
            var alive = rows.Values.Count(r => r.IsAlive);
            StatusText = $"{done}/{rows.Count} scanned · {alive} discovered";
        });

        try
        {
            await ScanRunner.RunAsync(options, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        finally
        {
            _finished = DateTimeOffset.Now;
            IsRunning = false;
            var alive = Results.Count(r => r.IsAlive);
            StatusText = $"{Results.Count} address(es) · {alive} discovered · {Results.Sum(r => r.OpenPorts.Count)} open port(s)";
            ExportHtmlCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Stop() => _cts?.Cancel();

    private ScanReport BuildReport()
    {
        var now = DateTimeOffset.Now;
        return new ScanReport(
            now,
            Targets,
            _started,
            _finished ?? now,
            PortList.Parse(PortsText),
            Results.ToList(),
            PingTimeout,
            PortTimeout,
            Math.Max(1, Concurrency));
    }

    private void ExportHtml()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML report (*.html)|*.html",
            FileName = "PingViewScan-Scan.html"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        File.WriteAllText(dialog.FileName, ScanReportExporter.Html(BuildReport()));
        OpenInBrowser(dialog.FileName);
    }

    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = "PingViewScan-Scan.csv"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        File.WriteAllText(dialog.FileName, ScanCsv());
    }

    private string ScanCsv()
    {
        var lines = new List<string> { "IP,Hostname,Alive,Ping_ms,Open_Ports" };
        foreach (var r in Results)
        {
            var ping = r.PingMs is { } ms ? ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            var ports = string.Join(" ", r.OpenPorts);
            lines.Add($"{Csv(r.Ip)},{Csv(r.Hostname ?? string.Empty)},{(r.IsAlive ? "1" : "0")},{ping},{ports}");
        }
        return string.Join("\n", lines) + "\n";
    }

    private static string Csv(string value)
    {
        var field = value;
        if (field.Length > 0 && "=+-@\t\r".IndexOf(field[0]) >= 0)
        {
            field = "'" + field;
        }
        return field.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
    }

    private static void OpenInBrowser(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}
