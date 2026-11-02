using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PingViewScan.Core;

namespace PingViewScan.App.ViewModels;

/// <summary>One node in the on-screen hop path (source, router or target).</summary>
public sealed record TraceNode(
    string Title,
    string Subtitle,
    string Detail,
    Brush Fill,
    Brush TextColor,
    Brush Connector,
    bool ShowConnector);

/// <summary>Backs the graphical traceroute tab.</summary>
public sealed class TracerouteViewModel : ObservableObject
{
    private static readonly Brush WhiteBrush = Brushes.White;
    private static readonly Brush MutedBrush = BrushFromHex("#94a3b8");
    private static readonly Brush BlueBrush = BrushFromHex("#2563eb");
    private static readonly Brush GreenBrush = BrushFromHex("#16a34a");
    private static readonly Brush GrayBrush = BrushFromHex("#cbd5e1");
    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<int, HopLatencyStats> _statsStore = new();

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _delayCts;
    private string _target = "1.1.1.1";
    private int _maxHops = 30;
    private int _queries = 3;
    private double _timeout = 2;
    private bool _continuous;
    private double _interval = 5;
    private bool _isRunning;
    private string _message = "Enter a host or IP address and press Trace.";
    private string? _resolvedIP;
    private int _rounds;
    private int _chartVersion;
    private string _tracedTarget = string.Empty;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;
    private bool _stoppedByUser;

    public TracerouteViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        ToggleCommand = new RelayCommand(_ => Toggle());
        ExportHtmlCommand = new RelayCommand(_ => ExportHtml(), _ => Hops.Count > 0);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => Hops.Count > 0);
        ClearCommand = new RelayCommand(_ => Clear(), _ => Hops.Count > 0 && !IsRunning);
    }

    public ObservableCollection<TracerouteHop> Hops { get; } = new();
    public ObservableCollection<TraceNode> Nodes { get; } = new();
    public ObservableCollection<HopLatencyStats> Stats { get; } = new();

    public RelayCommand ToggleCommand { get; }
    public RelayCommand ExportHtmlCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ClearCommand { get; }

    public IReadOnlyList<int> MaxHopsOptions { get; } = new[] { 16, 24, 30, 32, 48, 64 };
    public IReadOnlyList<int> QueryOptions { get; } = new[] { 1, 2, 3, 5 };
    public IReadOnlyList<double> TimeoutOptions { get; } = new[] { 1.0, 2.0, 3.0, 5.0 };
    public IReadOnlyList<double> IntervalOptions { get; } = new[] { 2.0, 5.0, 10.0, 30.0, 60.0 };

    public string Target
    {
        get => _target;
        set => Set(ref _target, value);
    }

    public int MaxHops
    {
        get => _maxHops;
        set => Set(ref _maxHops, value);
    }

    public int Queries
    {
        get => _queries;
        set => Set(ref _queries, value);
    }

    public double Timeout
    {
        get => _timeout;
        set => Set(ref _timeout, value);
    }

    public bool Continuous
    {
        get => _continuous;
        set
        {
            if (!Set(ref _continuous, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowInterval));
            if (value)
            {
                if (!IsRunning)
                {
                    Start();
                }
            }
            else if (IsRunning)
            {
                // If we're between passes, wake the delay so the loop can end now.
                _delayCts?.Cancel();
            }
        }
    }

    public bool ShowInterval => Continuous;

    public double Interval
    {
        get => _interval;
        set => Set(ref _interval, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(ToggleLabel));
                ToggleCommand.RaiseCanExecuteChanged();
                ClearCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ToggleLabel => IsRunning ? "Stop" : "Trace";

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public string? ResolvedIP
    {
        get => _resolvedIP;
        private set => Set(ref _resolvedIP, value);
    }

    public int Rounds
    {
        get => _rounds;
        private set => Set(ref _rounds, value);
    }

    public int ChartVersion
    {
        get => _chartVersion;
        private set => Set(ref _chartVersion, value);
    }

    public void Toggle()
    {
        if (IsRunning)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        var host = (Target ?? string.Empty).Trim();
        if (host.Length == 0 || host.StartsWith('-'))
        {
            Message = "Enter a valid host name or IP address.";
            return;
        }

        ResetAll(host);
        _cts = new CancellationTokenSource();
        _stoppedByUser = false;
        IsRunning = true;
        _ = RunAsync(host, _cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _stoppedByUser = true;
        if (_continuous)
        {
            _continuous = false;
            OnPropertyChanged(nameof(Continuous));
            OnPropertyChanged(nameof(ShowInterval));
        }
        _delayCts?.Cancel();
        _cts?.Cancel();
    }

    public void Clear()
    {
        if (IsRunning)
        {
            return;
        }

        Hops.Clear();
        Nodes.Clear();
        Stats.Clear();
        _statsStore.Clear();
        Rounds = 0;
        ResolvedIP = null;
        _startedAt = null;
        _finishedAt = null;
        Message = "Cleared.";
        ChartVersion++;
        RefreshCommands();
    }

    private async Task RunAsync(string host, CancellationToken ct)
    {
        try
        {
            do
            {
                var outcome = await TracerouteRunner.TraceAsync(
                    host,
                    Queries,
                    MaxHops,
                    Timeout,
                    ip =>
                    {
                        if (ip is not null)
                        {
                            _dispatcher.BeginInvoke(() =>
                            {
                                ResolvedIP = ip;
                                RebuildNodes();
                            });
                        }
                    },
                    hop => _dispatcher.BeginInvoke(() => AppendHop(hop)),
                    ct).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                _dispatcher.Invoke(() =>
                {
                    if (outcome.ResolvedIP is not null)
                    {
                        ResolvedIP = outcome.ResolvedIP;
                    }
                    Rounds++;
                    RebuildNodes();
                });

                if (!Continuous)
                {
                    break;
                }

                _dispatcher.Invoke(() =>
                    Message = $"Round {Rounds} complete — next pass in {Interval:g}s…");

                _delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, Interval)), _delayCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    break;
                }
                finally
                {
                    _delayCts.Dispose();
                    _delayCts = null;
                }

                if (!Continuous)
                {
                    break;
                }

                _dispatcher.Invoke(ResetPass);
            } while (!ct.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user.
        }
        catch (Exception ex)
        {
            _ = _dispatcher.BeginInvoke(() => Message = $"Trace failed: {ex.Message}");
        }
        finally
        {
            _ = _dispatcher.BeginInvoke(CompleteRun);
        }
    }

    private void CompleteRun()
    {
        IsRunning = false;
        _finishedAt = DateTimeOffset.Now;

        var responded = Hops.Count(h => !h.IsTimeout);
        if (_stoppedByUser)
        {
            Message = $"Stopped — {Rounds} round(s), {Hops.Count} hop(s) traced.";
        }
        else if (!Continuous && Rounds > 1)
        {
            Message = $"Continuous tracing stopped after {Rounds} round(s).";
        }
        else if (Hops.Count == 0)
        {
            Message = "Trace complete — no hops recorded.";
        }
        else
        {
            var result = ResolvedIP is { } ip && Hops.Any(h => h.Ip == ip && !h.IsTimeout)
                ? "target reached"
                : $"target not reached within {MaxHops} hops";
            Message = $"Trace complete — {Hops.Count} hop(s), {responded} responding, {result}.";
        }

        _cts?.Dispose();
        _cts = null;
        RefreshCommands();
    }

    private void ResetAll(string host)
    {
        Hops.Clear();
        Nodes.Clear();
        Stats.Clear();
        _statsStore.Clear();
        Rounds = 0;
        ResolvedIP = null;
        _tracedTarget = host;
        _startedAt = DateTimeOffset.Now;
        _finishedAt = null;
        Message = $"Tracing route to {host}…";
        ChartVersion++;
        RefreshCommands();
    }

    private void ResetPass()
    {
        Hops.Clear();
        Nodes.Clear();
        Message = $"Round {Rounds + 1} — tracing {_tracedTarget}…";
        RebuildNodes();
        RefreshCommands();
    }

    private void AppendHop(TracerouteHop hop)
    {
        var existing = Hops.FirstOrDefault(h => h.Ttl == hop.Ttl);
        if (existing is not null)
        {
            // The same TTL was already published; keep the newest snapshot but
            // don't fold its probes into the aggregate twice.
            Hops[Hops.IndexOf(existing)] = hop;
        }
        else
        {
            Hops.Add(hop);
            if (!_statsStore.TryGetValue(hop.Ttl, out var aggregate))
            {
                aggregate = new HopLatencyStats(hop.Ttl);
                _statsStore[hop.Ttl] = aggregate;
            }
            aggregate.Merge(hop);
            RebuildStats();
        }

        RebuildNodes();
        Message = $"Tracing route to {_tracedTarget}… {Hops.Count} hop(s) so far.";
        RefreshCommands();
    }

    private void RebuildStats()
    {
        Stats.Clear();
        foreach (var stat in _statsStore.Values.OrderBy(s => s.Ttl))
        {
            Stats.Add(stat);
        }
        ChartVersion++;
    }

    private void RebuildNodes()
    {
        Nodes.Clear();
        Nodes.Add(new TraceNode("You", "this PC", "source", BlueBrush, WhiteBrush, GrayBrush, false));

        foreach (var hop in Hops.OrderBy(h => h.Ttl))
        {
            var fill = BrushFromHex(hop.LatencyHex);
            var text = hop.IsTimeout ? MutedBrush : WhiteBrush;
            Nodes.Add(new TraceNode(
                hop.Ttl.ToString(),
                hop.DisplayHost,
                hop.MinRtt is { } min ? $"{min:0.0} ms" : "—",
                fill,
                text,
                fill,
                true));
        }

        if (ResolvedIP is { } resolved &&
            Hops.Any(h => h.Ip == resolved && !h.IsTimeout) &&
            Hops.LastOrDefault()?.Ip != resolved)
        {
            Nodes.Add(new TraceNode("Target", resolved, "reached", GreenBrush, WhiteBrush, GrayBrush, true));
        }
    }

    private void RefreshCommands()
    {
        ExportHtmlCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
    }

    public TracerouteReport BuildReport()
    {
        return new TracerouteReport(
            _tracedTarget.Length > 0 ? _tracedTarget : Target,
            ResolvedIP,
            DateTimeOffset.Now,
            _startedAt,
            _finishedAt,
            MaxHops,
            Queries,
            Timeout,
            Hops.ToList(),
            Stats.ToList(),
            Math.Max(1, Rounds));
    }

    private void ExportHtml()
    {
        if (Hops.Count == 0)
        {
            Message = "Nothing to report yet.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "HTML report (*.html)|*.html",
            FileName = $"PingViewScan-Trace-{Timestamp()}.html"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, TracerouteReportExporter.Html(BuildReport()));
        Message = $"Saved {Path.GetFileName(dialog.FileName)}";
        OpenInBrowser(dialog.FileName);
    }

    private void ExportCsv()
    {
        if (Hops.Count == 0)
        {
            Message = "Nothing to export.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"PingViewScan-Trace-{Timestamp()}.csv"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, TracerouteReportExporter.Csv(BuildReport()));
        Message = $"Saved {Path.GetFileName(dialog.FileName)}";
    }

    private static string Timestamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

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

    private static Brush BrushFromHex(string hex)
    {
        if (BrushCache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        BrushCache[hex] = brush;
        return brush;
    }
}
