using System.Globalization;
using System.Text;

namespace PingViewScan.Core;

/// <summary>
/// Minimal inline-SVG chart generator used by the HTML reports (no XML prolog,
/// so the output can be embedded directly in a web page).
/// </summary>
public static class Svg
{
    public sealed record Series(string Name, string Color, IReadOnlyList<(DateTimeOffset Time, double Value)> Points);

    private const string FontStack =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    public static double NiceMax(double value)
    {
        if (value <= 0)
        {
            return 1;
        }
        var exponent = Math.Floor(Math.Log10(value));
        var @base = Math.Pow(10, exponent);
        var normalized = value / @base;
        double nice = normalized switch
        {
            < 1.0001 => 1,
            < 2.0001 => 2,
            < 5.0001 => 5,
            _ => 10
        };
        return nice * @base;
    }

    public static string LineChart(
        double width,
        double height,
        string title,
        IReadOnlyList<Series> series,
        double yMin,
        double yMax,
        string yLabel,
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<DateTimeOffset>? lostMarkers = null)
    {
        const double marginLeft = 72;
        const double marginRight = 210;
        const double marginTop = 46;
        const double marginBottom = 50;
        var plotWidth = Math.Max(10, width - marginLeft - marginRight);
        var plotHeight = Math.Max(10, height - marginTop - marginBottom);
        var timeSpan = Math.Max(0.001, (end - start).TotalSeconds);
        var valueSpan = Math.Max(0.0001, yMax - yMin);

        double XFor(DateTimeOffset time)
        {
            var ratio = (time - start).TotalSeconds / timeSpan;
            return marginLeft + Math.Clamp(ratio, 0, 1) * plotWidth;
        }

        double YFor(double value)
        {
            var ratio = (value - yMin) / valueSpan;
            return marginTop + (1 - Math.Clamp(ratio, 0, 1)) * plotHeight;
        }

        var svg = new StringBuilder();
        svg.Append($"<svg viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\" width=\"100%\" ");
        svg.Append($"preserveAspectRatio=\"xMidYMid meet\" role=\"img\" font-family=\"{FontStack}\">");
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" fill=\"#ffffff\"/>");
        svg.Append($"<text x=\"{Fmt(marginLeft)}\" y=\"26\" font-size=\"16\" font-weight=\"600\" fill=\"#111827\">{Escape(title)}</text>");

        const int ySteps = 5;
        for (var step = 0; step <= ySteps; step++)
        {
            var value = yMin + valueSpan * step / ySteps;
            var y = YFor(value);
            svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(y)}\" x2=\"{Fmt(marginLeft + plotWidth)}\" y2=\"{Fmt(y)}\" stroke=\"#eef0f3\"/>");
            svg.Append($"<text x=\"{Fmt(marginLeft - 10)}\" y=\"{Fmt(y + 4)}\" font-size=\"11\" fill=\"#6b7280\" text-anchor=\"end\">{TickLabel(value, valueSpan)}</text>");
        }

        const int xSteps = 6;
        for (var step = 0; step <= xSteps; step++)
        {
            var time = start.AddSeconds(timeSpan * step / xSteps);
            var x = XFor(time);
            svg.Append($"<line x1=\"{Fmt(x)}\" y1=\"{Fmt(marginTop)}\" x2=\"{Fmt(x)}\" y2=\"{Fmt(marginTop + plotHeight)}\" stroke=\"#f4f5f7\"/>");
            svg.Append($"<text x=\"{Fmt(x)}\" y=\"{Fmt(marginTop + plotHeight + 18)}\" font-size=\"11\" fill=\"#6b7280\" text-anchor=\"middle\">{TimeLabel(time)}</text>");
        }

        svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(marginTop)}\" x2=\"{Fmt(marginLeft)}\" y2=\"{Fmt(marginTop + plotHeight)}\" stroke=\"#9ca3af\"/>");
        svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(marginTop + plotHeight)}\" x2=\"{Fmt(marginLeft + plotWidth)}\" y2=\"{Fmt(marginTop + plotHeight)}\" stroke=\"#9ca3af\"/>");
        svg.Append($"<text transform=\"translate(18 {Fmt(marginTop + plotHeight / 2)}) rotate(-90)\" font-size=\"12\" fill=\"#374151\" text-anchor=\"middle\">{Escape(yLabel)}</text>");

        if (lostMarkers is not null)
        {
            foreach (var time in lostMarkers)
            {
                var x = XFor(time);
                svg.Append($"<line x1=\"{Fmt(x)}\" y1=\"{Fmt(marginTop + plotHeight)}\" x2=\"{Fmt(x)}\" y2=\"{Fmt(marginTop + plotHeight - 7)}\" stroke=\"#dc2626\" stroke-width=\"1.4\" opacity=\"0.8\"/>");
            }
        }

        foreach (var serie in series)
        {
            if (serie.Points.Count == 0)
            {
                continue;
            }
            if (serie.Points.Count == 1)
            {
                var point = serie.Points[0];
                svg.Append($"<circle cx=\"{Fmt(XFor(point.Time))}\" cy=\"{Fmt(YFor(point.Value))}\" r=\"3\" fill=\"{serie.Color}\"/>");
                continue;
            }

            var path = new StringBuilder();
            for (var index = 0; index < serie.Points.Count; index++)
            {
                var point = serie.Points[index];
                path.Append(index == 0 ? "M" : "L");
                path.Append($"{Fmt(XFor(point.Time))} {Fmt(YFor(point.Value))} ");
            }
            svg.Append($"<path d=\"{path.ToString().Trim()}\" fill=\"none\" stroke=\"{serie.Color}\" stroke-width=\"1.6\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        }

        var legendY = marginTop + 2;
        foreach (var serie in series)
        {
            var swatchX = marginLeft + plotWidth + 18;
            svg.Append($"<rect x=\"{Fmt(swatchX)}\" y=\"{Fmt(legendY - 9)}\" width=\"12\" height=\"12\" rx=\"3\" fill=\"{serie.Color}\"/>");
            svg.Append($"<text x=\"{Fmt(swatchX + 18)}\" y=\"{Fmt(legendY)}\" font-size=\"12\" fill=\"#374151\">{Escape(Truncate(serie.Name, 22))}</text>");
            legendY += 18;
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    public static string BarChart(
        double width,
        double height,
        string title,
        IReadOnlyList<(string Label, double Value, string Color)> bars,
        string yLabel,
        bool rotateLabels = false,
        bool integerValues = false,
        string valueSuffix = "")
    {
        const double marginLeft = 68;
        const double marginRight = 24;
        const double marginTop = 46;
        var marginBottom = rotateLabels ? 132 : 52;
        var plotWidth = Math.Max(10, width - marginLeft - marginRight);
        var plotHeight = Math.Max(10, height - marginTop - marginBottom);
        var maxValue = NiceMax(Math.Max(bars.Count == 0 ? 1 : bars.Max(b => b.Value), 0.0001));

        double YFor(double value) => marginTop + (1 - Math.Clamp(value / maxValue, 0, 1)) * plotHeight;

        string Label(double value) => integerValues
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : TickLabel(value, maxValue);

        var svg = new StringBuilder();
        svg.Append($"<svg viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\" width=\"100%\" ");
        svg.Append($"preserveAspectRatio=\"xMidYMid meet\" role=\"img\" font-family=\"{FontStack}\">");
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" fill=\"#ffffff\"/>");
        svg.Append($"<text x=\"{Fmt(marginLeft)}\" y=\"26\" font-size=\"16\" font-weight=\"600\" fill=\"#111827\">{Escape(title)}</text>");

        const int ySteps = 5;
        for (var step = 0; step <= ySteps; step++)
        {
            var value = maxValue * step / ySteps;
            var y = YFor(value);
            svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(y)}\" x2=\"{Fmt(marginLeft + plotWidth)}\" y2=\"{Fmt(y)}\" stroke=\"#eef0f3\"/>");
            svg.Append($"<text x=\"{Fmt(marginLeft - 10)}\" y=\"{Fmt(y + 4)}\" font-size=\"11\" fill=\"#6b7280\" text-anchor=\"end\">{Label(value)}</text>");
        }
        svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(marginTop)}\" x2=\"{Fmt(marginLeft)}\" y2=\"{Fmt(marginTop + plotHeight)}\" stroke=\"#9ca3af\"/>");
        svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(marginTop + plotHeight)}\" x2=\"{Fmt(marginLeft + plotWidth)}\" y2=\"{Fmt(marginTop + plotHeight)}\" stroke=\"#9ca3af\"/>");
        svg.Append($"<text transform=\"translate(18 {Fmt(marginTop + plotHeight / 2)}) rotate(-90)\" font-size=\"12\" fill=\"#374151\" text-anchor=\"middle\">{Escape(yLabel)}</text>");

        if (bars.Count > 0)
        {
            var slot = plotWidth / bars.Count;
            var barWidth = Math.Min(slot * 0.72, 64);
            for (var index = 0; index < bars.Count; index++)
            {
                var bar = bars[index];
                var centerX = marginLeft + slot * (index + 0.5);
                var top = YFor(bar.Value);
                var barHeight = marginTop + plotHeight - top;
                svg.Append($"<rect x=\"{Fmt(centerX - barWidth / 2)}\" y=\"{Fmt(top)}\" width=\"{Fmt(barWidth)}\" height=\"{Fmt(Math.Max(0, barHeight))}\" rx=\"2\" fill=\"{bar.Color}\"/>");
                if (bar.Value > 0)
                {
                    svg.Append($"<text x=\"{Fmt(centerX)}\" y=\"{Fmt(top - 4)}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#6b7280\">{Label(bar.Value)}{Escape(valueSuffix)}</text>");
                }
                if (rotateLabels)
                {
                    svg.Append($"<text transform=\"translate({Fmt(centerX)} {Fmt(marginTop + plotHeight + 6)}) rotate(-60)\" text-anchor=\"end\" font-size=\"10\" fill=\"#6b7280\">{Escape(Truncate(bar.Label, 20))}</text>");
                }
                else
                {
                    svg.Append($"<text x=\"{Fmt(centerX)}\" y=\"{Fmt(marginTop + plotHeight + 16)}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#6b7280\">{Escape(Truncate(bar.Label, 18))}</text>");
                }
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string TickLabel(double value, double span)
    {
        if (span >= 20)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }
        if (span >= 5)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string TimeLabel(DateTimeOffset time) => time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..(maxLength - 1)] + "\u2026" : value;
}
