using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PingViewScan.Core;

namespace PingViewScan.App.Controls;

/// <summary>Grouped min / avg / max latency bars per traceroute hop.</summary>
public sealed class LatencyBars : FrameworkElement
{
    private static readonly Brush MinBrush = Freeze("#16a34a");
    private static readonly Brush AvgBrush = Freeze("#d97706");
    private static readonly Brush MaxBrush = Freeze("#dc2626");
    private static readonly Brush AxisBrush = Freeze("#9ca3af");
    private static readonly Brush GridBrush = Freeze("#eef0f3");
    private static readonly Brush LabelBrush = Freeze("#6b7280");
    private static readonly Brush PlaceholderBrush = Freeze("#9ca3af");

    public static readonly DependencyProperty StatsProperty = DependencyProperty.Register(
        nameof(Stats), typeof(IEnumerable<HopLatencyStats>), typeof(LatencyBars),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(LatencyBars),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<HopLatencyStats>? Stats
    {
        get => (IEnumerable<HopLatencyStats>?)GetValue(StatsProperty);
        set => SetValue(StatsProperty, value);
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
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var stats = (Stats ?? Enumerable.Empty<HopLatencyStats>())
            .Where(s => s.MinRtt is not null)
            .OrderBy(s => s.Ttl)
            .ToList();

        if (stats.Count == 0)
        {
            var text = MakeText("No latency data yet — run a trace to see min / avg / max per hop.",
                12, PlaceholderBrush, pixelsPerDip);
            dc.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
            return;
        }

        const double marginLeft = 54;
        const double marginRight = 12;
        const double marginTop = 24;
        const double marginBottom = 34;
        var plotWidth = Math.Max(10, width - marginLeft - marginRight);
        var plotHeight = Math.Max(10, height - marginTop - marginBottom);

        var max = stats
            .SelectMany(s => new[] { s.MinRtt, s.AvgRtt, s.MaxRtt })
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .DefaultIfEmpty(1)
            .Max();
        var yMax = Svg.NiceMax(Math.Max(max * 1.08, 1));

        double YFor(double value) => marginTop + (1 - Math.Clamp(value / yMax, 0, 1)) * plotHeight;

        for (var step = 0; step <= 4; step++)
        {
            var value = yMax * step / 4;
            var y = YFor(value);
            dc.DrawLine(new Pen(GridBrush, 1), new Point(marginLeft, y), new Point(marginLeft + plotWidth, y));
            var label = MakeText(TickLabel(value, yMax), 10, LabelBrush, pixelsPerDip);
            dc.DrawText(label, new Point(marginLeft - 8 - label.Width, y - 7));
        }

        dc.DrawLine(new Pen(AxisBrush, 1), new Point(marginLeft, marginTop),
            new Point(marginLeft, marginTop + plotHeight));
        dc.DrawLine(new Pen(AxisBrush, 1), new Point(marginLeft, marginTop + plotHeight),
            new Point(marginLeft + plotWidth, marginTop + plotHeight));

        var slot = plotWidth / stats.Count;
        var groupWidth = Math.Min(slot * 0.72, 90);
        var barWidth = groupWidth / 3;
        var bottomY = YFor(0);

        for (var index = 0; index < stats.Count; index++)
        {
            var stat = stats[index];
            var centerX = marginLeft + slot * (index + 0.5);

            DrawBar(dc, centerX - groupWidth / 2, YFor(stat.MinRtt!.Value), barWidth - 1.5, bottomY, MinBrush);
            if (stat.AvgRtt is { } avg)
            {
                DrawBar(dc, centerX - groupWidth / 2 + barWidth, YFor(avg), barWidth - 1.5, bottomY, AvgBrush);
            }
            if (stat.MaxRtt is { } maxRtt)
            {
                DrawBar(dc, centerX - groupWidth / 2 + barWidth * 2, YFor(maxRtt), barWidth - 1.5, bottomY, MaxBrush);
            }

            var labelText = stat.Ttl.ToString(CultureInfo.InvariantCulture);
            var label = MakeText(labelText, 10, LabelBrush, pixelsPerDip);
            var labelY = marginTop + plotHeight + 6;
            if (stats.Count > 12)
            {
                dc.PushTransform(new RotateTransform(-60, centerX, labelY));
                dc.DrawText(label, new Point(centerX - label.Width, labelY));
                dc.Pop();
            }
            else
            {
                dc.DrawText(label, new Point(centerX - label.Width / 2, labelY));
            }
        }

        // Legend: Min / Avg / Max.
        var legendX = width - marginRight;
        foreach (var (label, brush) in new[] { ("Max", MaxBrush), ("Avg", AvgBrush), ("Min", MinBrush) })
        {
            var text = MakeText(label, 11, LabelBrush, pixelsPerDip);
            legendX -= text.Width + 20;
            dc.DrawRectangle(brush, null, new Rect(legendX, 8, 11, 11));
            dc.DrawText(text, new Point(legendX + 16, 5));
        }
    }

    private static void DrawBar(DrawingContext dc, double x, double top, double width,
        double bottom, Brush brush)
    {
        var barHeight = Math.Max(1, bottom - top);
        dc.DrawRoundedRectangle(brush, null, new Rect(x, top, Math.Max(1, width), barHeight), 2, 2);
    }

    private static string TickLabel(double value, double span)
    {
        if (span >= 20) return value.ToString("0", CultureInfo.InvariantCulture);
        if (span >= 5) return value.ToString("0.0", CultureInfo.InvariantCulture);
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, pixelsPerDip);

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
