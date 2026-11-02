using System.Globalization;
using System.Text;

namespace PingViewScan.Core;

/// <summary>
/// Builds a self-contained HTML report (with an inline SVG hop-path diagram and
/// latency charts) and a CSV export from a <see cref="TracerouteReport"/>.
/// </summary>
public static class TracerouteReportExporter
{
    public static string Html(TracerouteReport report)
    {
        var path = HopPathSvg(report);
        var latencyChart = LatencyGroupedBars(report);

        var hopRows = string.Join("\n", report.Hops.Select(hop =>
        {
            var status = hop.IsTimeout
                ? "<span class=\"muted\">no response</span>"
                : $"<span class=\"good\">{Svg.Escape(HostLabel(hop))}</span>";
            return $"""
            <tr>
              <td class="mono">{hop.Ttl}</td>
              <td>{status}</td>
              <td class="mono">{Svg.Escape(hop.Ip ?? "—")}</td>
              <td>{hop.Sent}</td>
              <td>{hop.Received}</td>
              <td>{hop.Timeouts}</td>
              <td class="{(hop.LossPercent > 0 ? "bad" : "")}">{Ms(hop.LossPercent)}%</td>
              <td class="mono">{Ms(hop.LastRtt)}</td>
              <td class="mono">{Ms(hop.MinRtt)}</td>
              <td class="mono">{Ms(hop.AvgRtt)}</td>
              <td class="mono">{Ms(hop.MaxRtt)}</td>
            </tr>
            """;
        }));

        var reachedBadge = report.ReachedTarget
            ? "<span class=\"good\">reached</span>"
            : $"<span class=\"muted\">not reached within {report.MaxHops} hops</span>";

        var passesCard = report.Rounds > 1
            ? $"<div class=\"card\"><div class=\"label\">Passes</div><div class=\"value\">{report.Rounds}</div></div>"
            : string.Empty;

        var aggregatePanel = report.Rounds > 1
            ? $$"""
              <div class="panel">
                <h2>Aggregated per hop ({{report.Rounds}} passes)</h2>
                <table>
                  <thead>
                    <tr><th>#</th><th>Host</th><th>IP</th><th>Sent</th><th>Recv</th><th>Lost</th><th>Loss %</th><th>Last</th><th>Min</th><th>Avg</th><th>Max</th></tr>
                  </thead>
                  <tbody>
            {{AggregateRows(report)}}
                  </tbody>
                </table>
              </div>
            """
            : string.Empty;

        var resolvedText = report.ResolvedIP is { } ip ? $" ({Svg.Escape(ip)})" : string.Empty;
        var passesText = report.Rounds > 1 ? $" · {report.Rounds} passes" : string.Empty;

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8"/>
        <meta name="viewport" content="width=device-width, initial-scale=1"/>
        <title>PingViewScan Traceroute Report</title>
        <style>
          :root { color-scheme: light; }
          * { box-sizing: border-box; }
          body { margin: 0; padding: 32px; background: #f8fafc; color: #111827;
                 font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
          .wrap { max-width: 1120px; margin: 0 auto; }
          h1 { font-size: 26px; margin: 0 0 4px; }
          .sub { color: #6b7280; font-size: 13px; margin-bottom: 24px; }
          .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; margin-bottom: 24px; }
          .card { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 14px 16px; }
          .card .label { font-size: 12px; color: #6b7280; text-transform: uppercase; letter-spacing: .04em; }
          .card .value { font-size: 22px; font-weight: 600; margin-top: 4px; overflow-wrap: anywhere; }
          .card .value.good { color: #16a34a; }
          .card .value.bad { color: #dc2626; }
          .panel { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 18px; margin-bottom: 24px; }
          .panel h2 { font-size: 15px; margin: 0 0 12px; }
          .chart { width: 100%; }
          table { width: 100%; border-collapse: collapse; font-size: 13px; }
          th, td { padding: 8px 10px; text-align: right; border-bottom: 1px solid #f1f3f5; white-space: nowrap; }
          th:nth-child(2), td:nth-child(2) { text-align: left; }
          th:nth-child(3), td:nth-child(3) { text-align: left; }
          th { font-size: 11px; text-transform: uppercase; letter-spacing: .04em; color: #6b7280; background: #f9fafb; }
          tbody tr:hover { background: #f9fafb; }
          .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
          .good { color: #16a34a; font-weight: 600; }
          .bad { color: #dc2626; font-weight: 600; }
          .muted { color: #9ca3af; }
          .dt { color: #6b7280; }
          footer { color: #9ca3af; font-size: 12px; text-align: center; margin-top: 8px; }
        </style>
        </head>
        <body>
        <div class="wrap">
          <h1>PingViewScan Traceroute Report</h1>
          <div class="sub">Target {{Svg.Escape(report.Target.Length == 0 ? "—" : report.Target)}}{{resolvedText}} · Generated {{ReportExporter.DateTime(report.Generated)}} · Max hops {{report.MaxHops}} · Probes/hop {{report.Queries}} · Timeout {{Fmt(report.Timeout)}}s{{passesText}}</div>

          <div class="cards">
            <div class="card"><div class="label">Hops</div><div class="value">{{report.Hops.Count}}</div></div>
            <div class="card"><div class="label">Responded</div><div class="value good">{{report.RespondedHops.Count}}</div></div>
            <div class="card"><div class="label">Result</div><div class="value" style="font-size:15px">{{reachedBadge}}</div></div>
            <div class="card"><div class="label">Probes</div><div class="value">{{report.ProbeCount}}</div></div>
            <div class="card"><div class="label">Timeouts</div><div class="value {{(report.TimeoutCount > 0 ? "bad" : "")}}">{{report.TimeoutCount}}</div></div>
            <div class="card"><div class="label">Duration</div><div class="value" style="font-size:15px">{{ReportExporter.DurationString(report.Duration.TotalSeconds)}}</div></div>
            <div class="card"><div class="label">Resolved IP</div><div class="value" style="font-size:15px">{{(report.ResolvedIP is { } resolved ? Svg.Escape(resolved) : "—")}}</div></div>
            <div class="card"><div class="label">Started</div><div class="value" style="font-size:15px">{{(report.Started is { } started ? ReportExporter.DateTime(started) : "—")}}</div></div>
            {{passesCard}}
          </div>

          <div class="panel chart">{{path}}</div>
          <div class="panel chart">{{latencyChart}}</div>
          {{aggregatePanel}}

          <div class="panel">
            <h2>Hops ({{report.Hops.Count}})</h2>
            <table>
              <thead>
                <tr><th>#</th><th>Host</th><th>IP</th><th>Sent</th><th>Recv</th><th>Lost</th><th>Loss %</th><th>Last</th><th>Min</th><th>Avg</th><th>Max</th></tr>
              </thead>
              <tbody>
        {{hopRows}}
              </tbody>
            </table>
          </div>

          <footer>PingViewScan for Windows · Node colours indicate round-trip latency; grey means no response.</footer>
        </div>
        </body>
        </html>
        """;
    }

    public static string Csv(TracerouteReport report)
    {
        var lines = new List<string>
        {
            "Hop,Host,IP,Sent,Received,Lost,Loss_percent,Last_ms,Min_ms,Avg_ms,Max_ms"
        };

        foreach (var stat in report.StatsOrHops)
        {
            lines.Add(string.Join(",", new[]
            {
                stat.Ttl.ToString(CultureInfo.InvariantCulture),
                CsvField(stat.Host ?? string.Empty),
                CsvField(stat.Ip ?? string.Empty),
                stat.Sent.ToString(CultureInfo.InvariantCulture),
                stat.Received.ToString(CultureInfo.InvariantCulture),
                stat.Timeouts.ToString(CultureInfo.InvariantCulture),
                Num(stat.LossPercent),
                stat.LastRtt is { } last ? last.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty,
                stat.MinRtt is { } min ? min.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty,
                stat.AvgRtt is { } avg ? avg.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty,
                stat.MaxRtt is { } max ? max.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty
            }));
        }

        return string.Join("\n", lines) + "\n";
    }

    // MARK: - Visualisations

    /// <summary>Horizontal node-and-connector diagram: source → each hop → target.</summary>
    private static string HopPathSvg(TracerouteReport report)
    {
        var steps = report.Hops;
        if (steps.Count == 0)
        {
            return "<svg viewBox=\"0 0 1020 90\"><text x=\"24\" y=\"46\" font-size=\"14\" fill=\"#6b7280\">No hops recorded.</text></svg>";
        }

        const double margin = 46.0;
        const double step = 150.0;
        const double nodeY = 66.0;
        const double radius = 17.0;
        var width = margin * 2 + step * (steps.Count + 1);
        const double height = 200.0;

        double X(int index) => margin + step * index + radius;

        var svg = new StringBuilder();
        svg.Append($"<svg viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\" width=\"100%\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" font-family=\"-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif\">");
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" fill=\"#ffffff\"/>");
        svg.Append($"<text x=\"{Fmt(margin)}\" y=\"24\" font-size=\"16\" font-weight=\"600\" fill=\"#111827\">Path to {Svg.Escape(report.ResolvedIP ?? report.Target)}</text>");

        var sourceX = X(0);
        svg.Append($"<circle cx=\"{Fmt(sourceX)}\" cy=\"{Fmt(nodeY)}\" r=\"{Fmt(radius)}\" fill=\"#2563eb\"/>");
        svg.Append($"<text x=\"{Fmt(sourceX)}\" y=\"{Fmt(nodeY + 4)}\" font-size=\"11\" font-weight=\"600\" fill=\"#ffffff\" text-anchor=\"middle\">You</text>");
        svg.Append($"<text x=\"{Fmt(sourceX)}\" y=\"{Fmt(nodeY + 34)}\" font-size=\"10\" fill=\"#6b7280\" text-anchor=\"middle\">this PC</text>");

        var previousX = sourceX;
        for (var index = 0; index < steps.Count; index++)
        {
            var hop = steps[index];
            var nodeX = X(index + 1);
            var color = hop.LatencyLevel.Hex();

            svg.Append($"<line x1=\"{Fmt(previousX + radius)}\" y1=\"{Fmt(nodeY)}\" x2=\"{Fmt(nodeX - radius)}\" y2=\"{Fmt(nodeY)}\" stroke=\"{color}\" stroke-width=\"4\" stroke-linecap=\"round\"/>");
            svg.Append($"<circle cx=\"{Fmt(nodeX)}\" cy=\"{Fmt(nodeY)}\" r=\"{Fmt(radius)}\" fill=\"{color}\"/>");
            var numberColor = hop.IsTimeout ? "#64748b" : "#ffffff";
            svg.Append($"<text x=\"{Fmt(nodeX)}\" y=\"{Fmt(nodeY + 4)}\" font-size=\"11\" font-weight=\"600\" fill=\"{numberColor}\" text-anchor=\"middle\">{hop.Ttl}</text>");
            svg.Append($"<text x=\"{Fmt(nodeX)}\" y=\"{Fmt(nodeY + 34)}\" font-size=\"10\" fill=\"#374151\" text-anchor=\"middle\">{Svg.Escape(Truncate(ShortHost(hop), 20))}</text>");
            if (hop.Ip is { } hopIp)
            {
                svg.Append($"<text x=\"{Fmt(nodeX)}\" y=\"{Fmt(nodeY + 48)}\" font-size=\"9\" fill=\"#9ca3af\" text-anchor=\"middle\">{Svg.Escape(hopIp)}</text>");
            }
            var rttText = hop.MinRtt is { } min ? $"{min.ToString("0.0", CultureInfo.InvariantCulture)} ms" : "—";
            svg.Append($"<text x=\"{Fmt(nodeX)}\" y=\"{Fmt(nodeY + 64)}\" font-size=\"10\" fill=\"#6b7280\" text-anchor=\"middle\">{rttText}</text>");

            previousX = nodeX;
        }

        var legendY = height - 20;
        var legendX = margin;
        svg.Append("<g font-size=\"11\" fill=\"#6b7280\">");
        foreach (var level in new[]
                 {
                     LatencyLevel.Fast, LatencyLevel.Moderate, LatencyLevel.Slow,
                     LatencyLevel.VerySlow, LatencyLevel.TimedOut
                 })
        {
            svg.Append($"<rect x=\"{Fmt(legendX)}\" y=\"{Fmt(legendY - 10)}\" width=\"12\" height=\"12\" rx=\"3\" fill=\"{level.Hex()}\"/>");
            var italic = level == LatencyLevel.TimedOut ? " font-style=\"italic\"" : string.Empty;
            svg.Append($"<text x=\"{Fmt(legendX + 17)}\" y=\"{Fmt(legendY)}\"{italic}>{Svg.Escape(level.Label())}</text>");
            legendX += 24 + level.Label().Length * 7;
        }
        svg.Append("</g>");
        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>Grouped min / avg / max bars per hop, drawn as inline SVG.</summary>
    private static string LatencyGroupedBars(TracerouteReport report)
    {
        var stats = report.StatsOrHops.Where(s => s.MinRtt is not null).ToList();
        var metrics = new[]
        {
            ("Min", "#16a34a"),
            ("Avg", "#d97706"),
            ("Max", "#dc2626")
        };

        const double width = 1020.0;
        const double height = 340.0;
        const double marginLeft = 68.0;
        const double marginRight = 24.0;
        const double marginTop = 52.0;
        const double marginBottom = 132.0;
        var plotWidth = Math.Max(10, width - marginLeft - marginRight);
        var plotHeight = Math.Max(10, height - marginTop - marginBottom);

        Func<HopLatencyStats, int, double?> value = (stat, index) => index switch
        {
            0 => stat.MinRtt,
            1 => stat.AvgRtt,
            _ => stat.MaxRtt
        };

        var maxValue = Svg.NiceMax(Math.Max(
            stats.SelectMany(s => new[] { s.MinRtt, s.AvgRtt, s.MaxRtt })
                .Where(v => v is not null)
                .Select(v => v!.Value)
                .DefaultIfEmpty(1)
                .Max(), 0.0001));

        double YFor(double v) => marginTop + (1 - Math.Clamp(v / maxValue, 0, 1)) * plotHeight;

        var svg = new StringBuilder();
        svg.Append($"<svg viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\" width=\"100%\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" font-family=\"-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif\">");
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" fill=\"#ffffff\"/>");
        svg.Append($"<text x=\"{Fmt(marginLeft)}\" y=\"26\" font-size=\"16\" font-weight=\"600\" fill=\"#111827\">Latency per hop — min / avg / max</text>");

        var legendX = width - marginRight;
        foreach (var (name, color) in metrics.Reverse())
        {
            legendX -= 54;
            svg.Append($"<rect x=\"{Fmt(legendX)}\" y=\"16\" width=\"12\" height=\"12\" rx=\"3\" fill=\"{color}\"/>");
            svg.Append($"<text x=\"{Fmt(legendX + 17)}\" y=\"26\" font-size=\"12\" fill=\"#374151\">{name}</text>");
        }

        const int ySteps = 5;
        for (var step = 0; step <= ySteps; step++)
        {
            var v = maxValue * step / ySteps;
            var y = YFor(v);
            svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(y)}\" x2=\"{Fmt(marginLeft + plotWidth)}\" y2=\"{Fmt(y)}\" stroke=\"#eef0f3\"/>");
            svg.Append($"<text x=\"{Fmt(marginLeft - 10)}\" y=\"{Fmt(y + 4)}\" font-size=\"11\" fill=\"#6b7280\" text-anchor=\"end\">{TickLabel(v, maxValue)}</text>");
        }
        svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(marginTop)}\" x2=\"{Fmt(marginLeft)}\" y2=\"{Fmt(marginTop + plotHeight)}\" stroke=\"#9ca3af\"/>");
        svg.Append($"<line x1=\"{Fmt(marginLeft)}\" y1=\"{Fmt(marginTop + plotHeight)}\" x2=\"{Fmt(marginLeft + plotWidth)}\" y2=\"{Fmt(marginTop + plotHeight)}\" stroke=\"#9ca3af\"/>");
        svg.Append($"<text transform=\"translate(18 {Fmt(marginTop + plotHeight / 2)}) rotate(-90)\" font-size=\"12\" fill=\"#374151\" text-anchor=\"middle\">Latency (ms)</text>");

        if (stats.Count == 0)
        {
            svg.Append($"<text x=\"{Fmt(marginLeft + plotWidth / 2)}\" y=\"{Fmt(marginTop + plotHeight / 2)}\" font-size=\"13\" fill=\"#9ca3af\" text-anchor=\"middle\">No responding hops.</text>");
            svg.Append("</svg>");
            return svg.ToString();
        }

        var slot = plotWidth / stats.Count;
        var groupWidth = Math.Min(slot * 0.72, 96);
        var barWidth = groupWidth / 3;

        for (var index = 0; index < stats.Count; index++)
        {
            var stat = stats[index];
            var centerX = marginLeft + slot * (index + 0.5);
            for (var metricIndex = 0; metricIndex < metrics.Length; metricIndex++)
            {
                if (value(stat, metricIndex) is not { } v)
                {
                    continue;
                }
                var x = centerX - groupWidth / 2 + barWidth * metricIndex;
                var top = YFor(v);
                var barHeight = marginTop + plotHeight - top;
                svg.Append($"<rect x=\"{Fmt(x)}\" y=\"{Fmt(top)}\" width=\"{Fmt(barWidth - 1.5)}\" height=\"{Fmt(Math.Max(0, barHeight))}\" rx=\"2\" fill=\"{metrics[metricIndex].Item2}\"/>");
            }
            var label = stat.Ip is { } statIp ? $"{stat.Ttl}. {statIp}" : stat.Ttl.ToString(CultureInfo.InvariantCulture);
            svg.Append($"<text transform=\"translate({Fmt(centerX)} {Fmt(marginTop + plotHeight + 6)}) rotate(-60)\" text-anchor=\"end\" font-size=\"10\" fill=\"#6b7280\">{Svg.Escape(Truncate(label, 22))}</text>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string AggregateRows(TracerouteReport report) =>
        string.Join("\n", report.StatsOrHops.Select(stat =>
        {
            var status = stat.MinRtt is null
                ? "<span class=\"muted\">no response</span>"
                : $"<span class=\"good\">{Svg.Escape(stat.DisplayHost)}</span>";
            return $"""
            <tr>
              <td class="mono">{stat.Ttl}</td>
              <td>{status}</td>
              <td class="mono">{Svg.Escape(stat.Ip ?? "—")}</td>
              <td>{stat.Sent}</td>
              <td>{stat.Received}</td>
              <td>{stat.Timeouts}</td>
              <td class="{(stat.LossPercent > 0 ? "bad" : "")}">{Ms(stat.LossPercent)}%</td>
              <td class="mono">{Ms(stat.LastRtt)}</td>
              <td class="mono">{Ms(stat.MinRtt)}</td>
              <td class="mono">{Ms(stat.AvgRtt)}</td>
              <td class="mono">{Ms(stat.MaxRtt)}</td>
            </tr>
            """;
        }));

    private static string TickLabel(double value, double span)
    {
        if (span >= 20) return value.ToString("0", CultureInfo.InvariantCulture);
        if (span >= 5) return value.ToString("0.0", CultureInfo.InvariantCulture);
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string ShortHost(TracerouteHop hop) => hop.Host ?? hop.Ip ?? "*";

    private static string HostLabel(TracerouteHop hop)
    {
        if (hop.Host is { } host && host != hop.Ip)
        {
            return host;
        }
        return hop.Ip ?? "*";
    }

    private static string Ms(double? value) =>
        value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "—";

    private static string Ms(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Num(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string CsvField(string value)
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

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..(maxLength - 1)] + "\u2026" : value;
}
