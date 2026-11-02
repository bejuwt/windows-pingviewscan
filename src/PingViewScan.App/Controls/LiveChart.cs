using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PingViewScan.Core;

namespace PingViewScan.App.Controls;

/// <summary>
/// Lightweight live chart drawn directly with WPF primitives: either latency
/// over time (one line per host) or cumulative packet loss over time.
/// </summary>
public sealed class LiveChart : FrameworkElement
{
    private static readonly Brush[] Palette =
    {
        Freeze("#2563eb"), Freeze("#dc2626"), Freeze("#16a34a"), Freeze("#d97706"),
        Freeze("#7c3aed"), Freeze("#0891b2"), Freeze("#db2777"), Freeze("#65a30d"),
        Freeze("#0d9488"), Freeze("#9333ea"), Freeze("#ea580c"), Freeze("#4f46e5")
    };

    private static readonly Brush AxisBrush = Freeze("#9ca3af");
    private static readonly Brush GridBrush = Freeze("#eef0f3");
    private static readonly Brush LabelBrush = Freeze("#6b7280");
    private static readonly Brush TitleBrush = Freeze("#111827");
    private static readonly Brush LostBrush = Freeze("#dc2626");
    private static readonly Brush PlaceholderBrush = Freeze("#9ca3af");

    public static readonly DependencyProperty HostsProperty = DependencyProperty.Register(
        nameof(Hosts), typeof(IEnumerable<Host>), typeof(LiveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedIdsProperty = DependencyProperty.Register(
        nameof(SelectedIds), typeof(IReadOnlyCollection<Guid>), typeof(LiveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowSelectedOnlyProperty = DependencyProperty.Register(
        nameof(ShowSelectedOnly), typeof(bool), typeof(LiveChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimeframeProperty = DependencyProperty.Register(
        nameof(Timeframe), typeof(ChartTimeframe), typeof(LiveChart),
        new FrameworkPropertyMetadata(ChartTimeframe.All, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(ChartMode), typeof(LiveChart),
        new FrameworkPropertyMetadata(ChartMode.Latency, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(LiveChart),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<Host>? Hosts
    {
        get => (IEnumerable<Host>?)GetValue(HostsProperty);
        set => SetValue(HostsProperty, value);
    }

    public IReadOnlyCollection<Guid>? SelectedIds
    {
        get => (IReadOnlyCollection<Guid>?)GetValue(SelectedIdsProperty);
        set => SetValue(SelectedIdsProperty, value);
    }

    public bool ShowSelectedOnly
    {
        get => (bool)GetValue(ShowSelectedOnlyProperty);
        set => SetValue(ShowSelectedOnlyProperty, value);
    }

    public ChartTimeframe Timeframe
    {
        get => (ChartTimeframe)GetValue(TimeframeProperty);
        set => SetValue(TimeframeProperty, value);
    }

    public ChartMode Mode
    {
        get => (ChartMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 40 || height < 40)
        {
            return;
        }

        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

        var hosts = (Hosts ?? Enumerable.Empty<Host>())
            .Where(h => !ShowSelectedOnly || (SelectedIds?.Contains(h.Id) ?? false))
            .ToList();

        var latest = hosts
            .SelectMany(h => h.Samples)
            .Select(s => (DateTimeOffset?)s.Timestamp)
            .DefaultIfEmpty(null)
            .Max();

        if (latest is null)
        {
            var message = ShowSelectedOnly && (SelectedIds is null || SelectedIds.Count == 0)
                ? "No hosts selected — select rows in the table to chart them."
                : Mode == ChartMode.Latency
                    ? "No latency data yet — press Start."
                    : "No packet-loss data yet — press Start.";
            DrawPlaceholder(dc, width, height, message);
            return;
        }

        const double marginLeft = 58;
        const double marginRight = 148;
        const double marginTop = 14;
        const double marginBottom = 28;
        var plotWidth = Math.Max(10, width - marginLeft - marginRight);
        var plotHeight = Math.Max(10, height - marginTop - marginBottom);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var timeframe = Timeframe ?? ChartTimeframe.All;
        var start = SeriesStart(hosts, timeframe, latest.Value);

        if (Mode == ChartMode.Latency)
        {
            DrawLatency(dc, hosts, timeframe, start, latest.Value, marginLeft, marginTop, plotWidth, plotHeight, pixelsPerDip);
        }
        else
        {
            DrawLoss(dc, hosts, timeframe, start, latest.Value, marginLeft, marginTop, plotWidth, plotHeight, pixelsPerDip);
        }

        DrawAxes(dc, start, latest.Value, marginLeft, marginTop, plotWidth, plotHeight, pixelsPerDip);
        DrawLegend(dc, hosts, marginLeft + plotWidth + 12, marginTop, pixelsPerDip);
    }

    /// <summary>
    /// Left edge of the time axis. A fixed timeframe starts at its cutoff; the
    /// "All" timeframe starts at the oldest sample in the session.
    /// </summary>
    private static DateTimeOffset SeriesStart(
        IReadOnlyList<Host> hosts,
        ChartTimeframe timeframe,
        DateTimeOffset latest)
    {
        if (timeframe.Cutoff(latest) is { } cutoff)
        {
            return cutoff;
        }

        var earliest = hosts
            .SelectMany(h => h.Samples)
            .Select(s => (DateTimeOffset?)s.Timestamp)
            .DefaultIfEmpty(null)
            .Min();

        return earliest is { } start && start < latest ? start : latest.AddSeconds(-1);
    }

    private static void DrawLatency(
        DrawingContext dc,
        IReadOnlyList<Host> hosts,
        ChartTimeframe timeframe,
        DateTimeOffset start,
        DateTimeOffset latest,
        double marginLeft,
        double marginTop,
        double plotWidth,
        double plotHeight,
        double pixelsPerDip)
    {
        var limit = ChartSampling.PerSeriesLimit(hosts.Count);
        var series = new List<(Host Host, IReadOnlyList<PingSample> Samples)>();
        var maxRtt = 0.0;

        foreach (var host in hosts)
        {
            var samples = ChartSampling.VisibleSamples(host.Samples, timeframe, latest, limit);
            series.Add((host, samples));
            foreach (var sample in samples)
            {
                if (sample.RttMs is { } rtt)
                {
                    maxRtt = Math.Max(maxRtt, rtt);
                }
            }
        }

        var yMax = NiceMax(Math.Max(maxRtt * 1.15, 1));
        var end = latest;
        if (end <= start)
        {
            end = start.AddSeconds(1);
        }

        // Horizontal grid + value labels.
        for (var step = 0; step <= 4; step++)
        {
            var value = yMax * step / 4;
            var y = YFor(value, 0, yMax, marginTop, plotHeight);
            dc.DrawLine(new Pen(GridBrush, 1), new Point(marginLeft, y), new Point(marginLeft + plotWidth, y));
            DrawText(dc, value.ToString("0.#", CultureInfo.InvariantCulture), marginLeft - 8, y - 7,
                10, LabelBrush, pixelsPerDip, TextAlignment.Right, plotWidth: 42);
        }

        for (var index = 0; index < series.Count; index++)
        {
            var brush = Palette[index % Palette.Length];
            var samples = series[index].Samples;
            var geometry = new StreamGeometry();
            var started = false;
            var pointCount = 0;

            using (var context = geometry.Open())
            {
                foreach (var sample in samples)
                {
                    if (sample.RttMs is not { } rtt)
                    {
                        continue;
                    }
                    var point = new Point(
                        XFor(sample.Timestamp, start, end, marginLeft, plotWidth),
                        YFor(rtt, 0, yMax, marginTop, plotHeight));
                    if (!started)
                    {
                        context.BeginFigure(point, false, false);
                        started = true;
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                    pointCount++;
                }
            }

            geometry.Freeze();
            if (pointCount == 1)
            {
                var only = samples.First(s => s.RttMs is not null);
                dc.DrawEllipse(brush, null, new Point(
                    XFor(only.Timestamp, start, end, marginLeft, plotWidth),
                    YFor(only.RttMs!.Value, 0, yMax, marginTop, plotHeight)), 2.5, 2.5);
            }
            else if (pointCount > 1)
            {
                dc.DrawGeometry(null, new Pen(brush, 1.6), geometry);
            }

            // Lost probes as small ticks along the bottom axis.
            foreach (var sample in samples)
            {
                if (sample.IsLost)
                {
                    var x = XFor(sample.Timestamp, start, end, marginLeft, plotWidth);
                    dc.DrawLine(new Pen(LostBrush, 1.2),
                        new Point(x, marginTop + plotHeight),
                        new Point(x, marginTop + plotHeight - 6));
                }
            }
        }
    }

    private static void DrawLoss(
        DrawingContext dc,
        IReadOnlyList<Host> hosts,
        ChartTimeframe timeframe,
        DateTimeOffset start,
        DateTimeOffset latest,
        double marginLeft,
        double marginTop,
        double plotWidth,
        double plotHeight,
        double pixelsPerDip)
    {
        var limit = ChartSampling.PerSeriesLimit(hosts.Count);
        var cutoff = timeframe.Cutoff(latest);
        var end = latest;
        if (end <= start)
        {
            end = start.AddSeconds(1);
        }

        for (var step = 0; step <= 4; step++)
        {
            var value = step * 25.0;
            var y = YFor(value, 0, 100, marginTop, plotHeight);
            dc.DrawLine(new Pen(GridBrush, 1), new Point(marginLeft, y), new Point(marginLeft + plotWidth, y));
            DrawText(dc, $"{value:0}%", marginLeft - 8, y - 7,
                10, LabelBrush, pixelsPerDip, TextAlignment.Right, plotWidth: 42);
        }

        for (var index = 0; index < hosts.Count; index++)
        {
            var host = hosts[index];
            var brush = Palette[index % Palette.Length];
            var points = new List<(DateTimeOffset Time, double Percent)>();
            var sent = 0;
            var lost = 0;

            foreach (var sample in host.Samples)
            {
                sent++;
                if (sample.IsLost)
                {
                    lost++;
                }
                if (cutoff is { } cut && sample.Timestamp < cut)
                {
                    continue;
                }
                points.Add((sample.Timestamp, (double)lost / sent * 100.0));
            }

            if (points.Count == 0)
            {
                continue;
            }

            var span = (points[^1].Time - points[0].Time).TotalSeconds;
            var width = ChartSampling.BucketWidth(timeframe, span, limit);
            var decimated = ChartSampling.Decimate(points, p => p.Time, width, limit);

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var pointIndex = 0; pointIndex < decimated.Count; pointIndex++)
                {
                    var point = new Point(
                        XFor(decimated[pointIndex].Time, start, end, marginLeft, plotWidth),
                        YFor(decimated[pointIndex].Percent, 0, 100, marginTop, plotHeight));
                    if (pointIndex == 0)
                    {
                        context.BeginFigure(point, false, false);
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                }
            }
            geometry.Freeze();

            if (decimated.Count == 1)
            {
                dc.DrawEllipse(brush, null, new Point(
                    XFor(decimated[0].Time, start, end, marginLeft, plotWidth),
                    YFor(decimated[0].Percent, 0, 100, marginTop, plotHeight)), 2.5, 2.5);
            }
            else
            {
                dc.DrawGeometry(null, new Pen(brush, 1.6), geometry);
            }
        }
    }

    private static void DrawAxes(
        DrawingContext dc,
        DateTimeOffset start,
        DateTimeOffset latest,
        double marginLeft,
        double marginTop,
        double plotWidth,
        double plotHeight,
        double pixelsPerDip)
    {
        var end = latest;
        if (end <= start)
        {
            end = start.AddSeconds(1);
        }

        dc.DrawLine(new Pen(AxisBrush, 1),
            new Point(marginLeft, marginTop),
            new Point(marginLeft, marginTop + plotHeight));
        dc.DrawLine(new Pen(AxisBrush, 1),
            new Point(marginLeft, marginTop + plotHeight),
            new Point(marginLeft + plotWidth, marginTop + plotHeight));

        const int xSteps = 5;
        for (var step = 0; step <= xSteps; step++)
        {
            var time = start.AddSeconds((end - start).TotalSeconds * step / xSteps);
            var x = marginLeft + plotWidth * step / xSteps;
            dc.DrawLine(new Pen(GridBrush, 1),
                new Point(x, marginTop), new Point(x, marginTop + plotHeight));
            DrawText(dc, time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                x - 28, marginTop + plotHeight + 7, 10, LabelBrush, pixelsPerDip, TextAlignment.Left, plotWidth: 56);
        }
    }

    private static void DrawLegend(
        DrawingContext dc,
        IReadOnlyList<Host> hosts,
        double x,
        double y,
        double pixelsPerDip)
    {
        const int maxEntries = 12;
        for (var index = 0; index < Math.Min(hosts.Count, maxEntries); index++)
        {
            var brush = Palette[index % Palette.Length];
            dc.DrawRectangle(brush, null, new Rect(x, y + index * 17 + 4, 11, 3));
            DrawText(dc, Truncate(hosts[index].Address, 20), x + 17, y + index * 17 - 2,
                11, TitleBrush, pixelsPerDip, TextAlignment.Left, plotWidth: 120);
        }

        if (hosts.Count > maxEntries)
        {
            DrawText(dc, $"+{hosts.Count - maxEntries} more", x, y + maxEntries * 17,
                10, LabelBrush, pixelsPerDip, TextAlignment.Left, plotWidth: 120);
        }
    }

    private void DrawPlaceholder(DrawingContext dc, double width, double height, string message)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = MakeText(message, 13, PlaceholderBrush, pixelsPerDip);
        dc.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }

    private static double XFor(DateTimeOffset time, DateTimeOffset start, DateTimeOffset end,
        double marginLeft, double plotWidth)
    {
        var span = Math.Max(0.001, (end - start).TotalSeconds);
        return marginLeft + Math.Clamp((time - start).TotalSeconds / span, 0, 1) * plotWidth;
    }

    private static double YFor(double value, double min, double max,
        double marginTop, double plotHeight)
    {
        var span = Math.Max(0.0001, max - min);
        return marginTop + (1 - Math.Clamp((value - min) / span, 0, 1)) * plotHeight;
    }

    private static double NiceMax(double value) => Svg.NiceMax(value);

    private static void DrawText(DrawingContext dc, string text, double x, double y,
        double size, Brush brush, double pixelsPerDip,
        TextAlignment alignment = TextAlignment.Left, double plotWidth = 0)
    {
        var formatted = MakeText(text, size, brush, pixelsPerDip);
        var offset = alignment == TextAlignment.Right ? -formatted.Width : 0;
        dc.DrawText(formatted, new Point(x + offset, y));
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, pixelsPerDip);

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..(maxLength - 1)] + "…" : value;

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
