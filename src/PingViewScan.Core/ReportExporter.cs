using System.Globalization;
using System.Text;

namespace PingViewScan.Core;

/// <summary>
/// Builds a self-contained HTML report (with inline SVG graphs) and a raw CSV
/// export from a <see cref="SessionReport"/>.
/// </summary>
public static class ReportExporter
{
    private static readonly string[] Palette =
    {
        "#2563eb", "#dc2626", "#16a34a", "#d97706", "#7c3aed", "#0891b2",
        "#db2777", "#65a30d", "#0d9488", "#9333ea", "#ea580c", "#4f46e5"
    };

    public static string Html(SessionReport report)
    {
        var allSamples = report.Hosts.SelectMany(h => h.Samples).ToList();
        var start = report.Started ?? (allSamples.Count > 0 ? allSamples.Min(s => s.Timestamp) : report.Generated);
        var rawEnd = report.Ended ?? (allSamples.Count > 0 ? allSamples.Max(s => s.Timestamp) : report.Generated);
        var end = rawEnd > start ? rawEnd : start.AddSeconds(1);

        var latencySeries = new List<Svg.Series>();
        var lossSeries = new List<Svg.Series>();
        var lostMarkers = new List<DateTimeOffset>();
        var maxRtt = 0.0;

        for (var index = 0; index < report.Hosts.Count; index++)
        {
            var host = report.Hosts[index];
            var color = Palette[index % Palette.Length];

            var points = host.Samples
                .Where(s => s.RttMs is not null)
                .Select(s => (s.Timestamp, s.RttMs!.Value))
                .ToList();
            if (points.Count > 0)
            {
                maxRtt = Math.Max(maxRtt, points.Max(p => p.Item2));
            }
            latencySeries.Add(new Svg.Series(host.Address, color, points));
            lostMarkers.AddRange(host.Samples.Where(s => s.IsLost).Select(s => s.Timestamp));

            var sent = 0;
            var lost = 0;
            var lossPoints = new List<(DateTimeOffset, double)>();
            foreach (var sample in host.Samples)
            {
                sent++;
                if (sample.IsLost)
                {
                    lost++;
                }
                lossPoints.Add((sample.Timestamp, (double)lost / sent * 100.0));
            }
            lossSeries.Add(new Svg.Series(host.Address, color, lossPoints));
        }

        var latencyYMax = Svg.NiceMax(Math.Max(maxRtt * 1.15, 1));
        var latencyChart = Svg.LineChart(1020, 360, "Latency over time", latencySeries, 0, latencyYMax, "Latency (ms)", start, end, lostMarkers);
        var lossChart = Svg.LineChart(1020, 320, "Packet loss over time (cumulative)", lossSeries, 0, 100, "Packet loss (%)", start, end);

        var summaryRows = new StringBuilder();
        for (var index = 0; index < report.Hosts.Count; index++)
        {
            var host = report.Hosts[index];
            var color = Palette[index % Palette.Length];
            var s = host.Stats;
            var lossClass = s.LossPercent > 0 ? "bad" : "good";
            summaryRows.Append($"""
            <tr>
              <td class="host"><span class="swatch" style="background:{color}"></span>{Svg.Escape(host.Address)}</td>
              <td class="mono">{Svg.Escape(host.Ip ?? "—")}</td>
              <td>{s.Sent}</td>
              <td>{s.Received}</td>
              <td>{s.Lost}</td>
              <td class="{lossClass}">{Ms(s.LossPercent)}%</td>
              <td class="mono">{Ms(s.Last)}</td>
              <td class="mono">{Ms(s.Min)}</td>
              <td class="mono">{Ms(s.Max)}</td>
              <td class="mono">{Ms(s.Average)}</td>
            </tr>
            """);
        }

        var totalSent = report.Hosts.Sum(h => h.Stats.Sent);
        var totalLost = report.Hosts.Sum(h => h.Stats.Lost);
        var overallLoss = totalSent > 0 ? (double)totalLost / totalSent * 100 : 0;
        var duration = (report.Ended ?? end) - (report.Started ?? start);
        var startText = report.Started is { } st ? DateTime(st) : "—";

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8"/>
        <meta name="viewport" content="width=device-width, initial-scale=1"/>
        <title>PingViewScan Report</title>
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
          .card .value { font-size: 22px; font-weight: 600; margin-top: 4px; }
          .card .value.bad { color: #dc2626; }
          .panel { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 18px; margin-bottom: 24px; }
          .panel h2 { font-size: 15px; margin: 0 0 12px; }
          table { width: 100%; border-collapse: collapse; font-size: 13px; }
          th, td { padding: 8px 10px; text-align: right; border-bottom: 1px solid #f1f3f5; white-space: nowrap; }
          th:first-child, td:first-child, th:nth-child(2), td:nth-child(2) { text-align: left; }
          th { font-size: 11px; text-transform: uppercase; letter-spacing: .04em; color: #6b7280; background: #f9fafb; }
          tbody tr:hover { background: #f9fafb; }
          .host { font-weight: 600; }
          .swatch { display: inline-block; width: 10px; height: 10px; border-radius: 3px; margin-right: 8px; }
          .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
          .good { color: #16a34a; }
          .bad { color: #dc2626; font-weight: 600; }
          .chart { width: 100%; }
          footer { color: #9ca3af; font-size: 12px; text-align: center; margin-top: 8px; }
        </style>
        </head>
        <body>
        <div class="wrap">
          <h1>PingViewScan Report</h1>
          <div class="sub">Generated {{DateTime(report.Generated)}} · Interval {{Fmt(report.Interval)}}s · Timeout {{Fmt(report.Timeout)}}s</div>

          <div class="cards">
            <div class="card"><div class="label">Hosts</div><div class="value">{{report.Hosts.Count}}</div></div>
            <div class="card"><div class="label">Duration</div><div class="value">{{DurationString(duration.TotalSeconds)}}</div></div>
            <div class="card"><div class="label">Probes sent</div><div class="value">{{totalSent}}</div></div>
            <div class="card"><div class="label">Packets lost</div><div class="value {{(totalLost > 0 ? "bad" : "")}}">{{totalLost}}</div></div>
            <div class="card"><div class="label">Overall loss</div><div class="value {{(overallLoss > 0 ? "bad" : "")}}">{{Ms(overallLoss)}}%</div></div>
            <div class="card"><div class="label">Session start</div><div class="value" style="font-size:15px">{{startText}}</div></div>
          </div>

          <div class="panel">
            <h2>Summary</h2>
            <table>
              <thead>
                <tr>
                  <th>Host</th><th>IP</th><th>Sent</th><th>Recv</th><th>Lost</th><th>Loss %</th>
                  <th>Last</th><th>Min</th><th>Max</th><th>Avg</th>
                </tr>
              </thead>
              <tbody>
        {{summaryRows}}
              </tbody>
            </table>
          </div>

          <div class="panel chart">{{latencyChart}}</div>
          <div class="panel chart">{{lossChart}}</div>

          <footer>PingViewScan for Windows · Red ticks on the latency chart mark lost packets.</footer>
        </div>
        </body>
        </html>
        """;
    }

    public static string Csv(SessionReport report)
    {
        var lines = new List<string> { "Host,IP,Sequence,Timestamp,RTT_ms,Lost" };
        foreach (var host in report.Hosts)
        {
            foreach (var sample in host.Samples)
            {
                var rtt = sample.RttMs is { } ms ? ms.ToString("0.000", CultureInfo.InvariantCulture) : "";
                var timestamp = sample.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                lines.Add($"{CsvField(host.Address)},{CsvField(host.Ip ?? "")},{sample.Sequence},{timestamp},{rtt},{(sample.IsLost ? "1" : "0")}");
            }
        }
        return string.Join("\n", lines) + "\n";
    }

    public static string DateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string DurationString(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var parts = new List<string>();
        if (span.TotalHours >= 1)
        {
            parts.Add($"{(int)span.TotalHours}h");
        }
        if (span.Minutes > 0 || span.TotalHours >= 1)
        {
            parts.Add($"{span.Minutes}m");
        }
        parts.Add($"{span.Seconds}s");
        return string.Join(" ", parts.Take(2));
    }

    private static string Ms(double? value) =>
        value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "—";

    private static string Ms(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Fmt(double value) => value.ToString("g", CultureInfo.InvariantCulture);

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
}
