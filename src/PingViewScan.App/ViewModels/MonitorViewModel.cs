using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PingViewScan.App.Views;
using PingViewScan.Core;

namespace PingViewScan.App.ViewModels;

public sealed class MonitorViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private DateTimeOffset? _started;
    private bool _isRunning;
    private string _newHost = string.Empty;
    private double _intervalSeconds = 1;
    private double _timeoutSeconds = 1;
    private string _statusText = "Idle";
    private Host? _selectedHost;

    public MonitorViewModel()
    {
        AddCommand = new RelayCommand(_ => AddFromInput(), _ => !string.IsNullOrWhiteSpace(NewHost));
        BulkAddCommand = new RelayCommand(_ => BulkAdd());
        RemoveCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedHost is not null);
        ClearCommand = new RelayCommand(_ => ClearAll(), _ => Hosts.Count > 0);
        ToggleRunningCommand = new RelayCommand(_ => ToggleRunning());
        ExportHtmlCommand = new RelayCommand(_ => ExportHtml(), _ => Hosts.Count > 0);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => Hosts.Count > 0);
    }

    public ObservableCollection<Host> Hosts { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand BulkAddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ToggleRunningCommand { get; }
    public RelayCommand ExportHtmlCommand { get; }
    public RelayCommand ExportCsvCommand { get; }

    public string NewHost
    {
        get => _newHost;
        set
        {
            if (Set(ref _newHost, value))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double IntervalSeconds
    {
        get => _intervalSeconds;
        set => Set(ref _intervalSeconds, value);
    }

    public double TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => Set(ref _timeoutSeconds, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(RunningLabel));
            }
        }
    }

    public string RunningLabel => IsRunning ? "Stop" : "Start";

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public Host? SelectedHost
    {
        get => _selectedHost;
        set
        {
            if (Set(ref _selectedHost, value))
            {
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void AddFromInput()
    {
        var entries = HostImport.Parse(NewHost, filterInvalid: false);
        AddHosts(entries);
        NewHost = string.Empty;
    }

    public void AddHosts(IEnumerable<string> addresses)
    {
        var existing = new HashSet<string>(Hosts.Select(h => h.Address), StringComparer.OrdinalIgnoreCase);
        foreach (var address in addresses)
        {
            if (existing.Add(address))
            {
                Hosts.Add(new Host(address));
            }
        }
        UpdateIdleStatus();
        ClearCommand.RaiseCanExecuteChanged();
        ExportHtmlCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
    }

    private void BulkAdd()
    {
        var dialog = new AddHostsDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
        {
            AddHosts(dialog.Result);
        }
    }

    private void RemoveSelected()
    {
        if (SelectedHost is { } host)
        {
            Hosts.Remove(host);
            UpdateIdleStatus();
            ClearCommand.RaiseCanExecuteChanged();
        }
    }

    private void ClearAll()
    {
        Hosts.Clear();
        UpdateIdleStatus();
        ClearCommand.RaiseCanExecuteChanged();
    }

    private async void ToggleRunning()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            return;
        }
        if (Hosts.Count == 0)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _started = DateTimeOffset.Now;
        IsRunning = true;
        StatusText = "Running";

        try
        {
            foreach (var host in Hosts)
            {
                host.Stats.Reset();
                host.Samples.Clear();
            }

            while (!_cts.IsCancellationRequested)
            {
                await PingRoundAsync(_cts.Token);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, IntervalSeconds)), _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            UpdateIdleStatus();
        }
    }

    private async Task PingRoundAsync(CancellationToken ct)
    {
        var targets = Hosts.Where(h => h.Enabled).ToList();
        var timeout = TimeoutSeconds;

        var probes = targets.Select(async host =>
        {
            host.Ip ??= await NetUtil.ResolveIpAsync(host.Address, ct);
            if (host.Ip is { } ip && host.Hostname is null)
            {
                host.Hostname = await NetUtil.ReverseDnsAsync(ip, ct);
            }
            var result = await PingRunner.PingAsync(host.Address, timeout, ct);
            return (host, result);
        });

        var outcomes = await Task.WhenAll(probes);
        var now = DateTimeOffset.Now;
        foreach (var (host, result) in outcomes)
        {
            host.Stats.Add(result.RttMs);
            host.Samples.Add(new PingSample(host.Samples.Count + 1, now, result.RttMs));
        }

        var sent = Hosts.Sum(h => h.Stats.Sent);
        var lost = Hosts.Sum(h => h.Stats.Lost);
        var alive = Hosts.Count(h => h.Stats.Received > 0);
        StatusText = $"{Hosts.Count} host(s) · {alive} responding · {sent} sent · {lost} lost";
    }

    private void UpdateIdleStatus()
    {
        if (!IsRunning)
        {
            StatusText = Hosts.Count == 0 ? "Idle" : $"{Hosts.Count} host(s) · Idle";
        }
    }

    private SessionReport BuildReport()
    {
        var now = DateTimeOffset.Now;
        return new SessionReport(
            now,
            _started,
            now,
            IntervalSeconds,
            TimeoutSeconds,
            Hosts.Select(h => new HostReport(h.Address, h.Ip, h.Stats, h.Samples)).ToList());
    }

    private void ExportHtml()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML report (*.html)|*.html",
            FileName = "PingViewScan-Report.html"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        File.WriteAllText(dialog.FileName, ReportExporter.Html(BuildReport()));
        OpenInBrowser(dialog.FileName);
    }

    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = "PingViewScan-Report.csv"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        File.WriteAllText(dialog.FileName, ReportExporter.Csv(BuildReport()));
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
