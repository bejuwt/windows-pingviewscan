using System.Globalization;
using System.Text;

namespace PingViewScan.Core;

/// <summary>Snapshot of a completed (or in-progress) network scan, used for reports.</summary>
public sealed record ScanReport(
    DateTimeOffset Generated,
    string Targets,
    DateTimeOffset? Started,
    DateTimeOffset? Finished,
    IReadOnlyList<ushort> Ports,
    IReadOnlyList<ScanResult> Results,
    double PingTimeout,
    double PortTimeout,
    int Concurrency)
{
    public int Total => Results.Count;
    public IReadOnlyList<ScanResult> Alive => Results.Where(r => r.IsAlive).ToList();
    public int AliveCount => Alive.Count;
    public double AlivePercent => Total > 0 ? (double)AliveCount / Total * 100.0 : 0.0;
    public TimeSpan Duration => (Finished ?? DateTimeOffset.Now) - (Started ?? Generated);
}

/// <summary>
/// Builds a self-contained HTML report (with inline SVG visualisations) for an
/// IP-block scan: subnet occupancy map, latency bars, port popularity and a
/// full results table.
/// </summary>
public static class ScanReportExporter
{
    private sealed record GridCell(string Ip, bool Alive, int PortCount, bool Scanned);

    public static string Html(ScanReport report)
    {
        var cells = report.Results
            .Select(r => new GridCell(r.Ip, r.IsAlive, r.OpenPorts.Count, r.Scanned))
            .ToList();

        var grid = SubnetGrid(cells);
        var latencyChart = LatencyBars(report.Alive);
        var portChart = PortBars(report.Alive, report.Ports);

        var resultsRows = new StringBuilder();
        foreach (var result in report.Results)
        {
            var ping = result.PingMs is { } ms ? ms.ToString("0.0", CultureInfo.InvariantCulture) : "—";
            var ports = result.OpenPorts.Count == 0
                ? "—"
                : string.Join(", ", result.OpenPorts.Select(p => p.ToString(CultureInfo.InvariantCulture)));
            var status = result.IsAlive
                ? "<span class=\"good\">up</span>"
                : "<span class=\"muted\">no response</span>";
            resultsRows.Append($"""
            <tr>
              <td class="mono">{Svg.Escape(result.Ip)}</td>
              <td>{Svg.Escape(result.Hostname ?? "—")}</td>
              <td>{status}</td>
              <td class="mono">{ping}</td>
              <td class="mono">{Svg.Escape(ports)}</td>
            </tr>
            """);
        }

        var openPortCount = report.Alive.Sum(r => r.OpenPorts.Count);
        var targetsText = report.Targets.Length == 0 ? "—" : Svg.Escape(report.Targets);
        var scanStart = report.Started is { } st ? ReportExporter.DateTime(st) : "—";
        var addressWord = report.Total == 1 ? "" : "es";

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
          .card .value.good { color: #16a34a; }
          .panel { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 18px; margin-bottom: 24px; }
          .panel h2 { font-size: 15px; margin: 0 0 12px; }
          .chart { width: 100%; }
          table { width: 100%; border-collapse: collapse; font-size: 13px; }
          th, td { padding: 8px 10px; text-align: right; border-bottom: 1px solid #f1f3f5; white-space: nowrap; }
          th:first-child, td:first-child, th:nth-child(2), td:nth-child(2) { text-align: left; }
          th { font-size: 11px; text-transform: uppercase; letter-spacing: .04em; color: #6b7280; background: #f9fafb; }
          tbody tr:hover { background: #f9fafb; }
          .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
          .good { color: #16a34a; font-weight: 600; }
          .muted { color: #9ca3af; }
          footer { color: #9ca3af; font-size: 12px; text-align: center; margin-top: 8px; }
        </style>
        </head>
        <body>
        <div class="wrap">
          <h1>PingViewScan Report</h1>
          <div class="sub">Targets {{targetsText}} · Generated {{ReportExporter.DateTime(report.Generated)}} · Ping timeout {{Fmt(report.PingTimeout)}}s · Port timeout {{Fmt(report.PortTimeout)}}s</div>

          <div class="cards">
            <div class="card"><div class="label">Addresses</div><div class="value">{{report.Total}}</div></div>
            <div class="card"><div class="label">Discovered</div><div class="value good">{{report.AliveCount}}</div></div>
            <div class="card"><div class="label">Alive</div><div class="value">{{report.AlivePercent.ToString("0", CultureInfo.InvariantCulture)}}%</div></div>
            <div class="card"><div class="label">Ports scanned</div><div class="value">{{report.Ports.Count}}</div></div>
            <div class="card"><div class="label">Open ports</div><div class="value">{{openPortCount}}</div></div>
            <div class="card"><div class="label">Duration</div><div class="value" style="font-size:15px">{{ReportExporter.DurationString(report.Duration.TotalSeconds)}}</div></div>
            <div class="card"><div class="label">Concurrency</div><div class="value">{{report.Concurrency}}</div></div>
            <div class="card"><div class="label">Scan start</div><div class="value" style="font-size:15px">{{scanStart}}</div></div>
          </div>

          <div class="panel chart">{{grid}}</div>
          <div class="panel chart">{{latencyChart}}</div>
          <div class="panel chart">{{portChart}}</div>

          <div class="panel">
            <h2>Results ({{report.Total}} address{{addressWord}})</h2>
            <table>
              <thead>
                <tr><th>IP address</th><th>Hostname</th><th>Status</th><th>Ping (ms)</th><th>Open ports</th></tr>
              </thead>
              <tbody>
        {{resultsRows}}
              </tbody>
            </table>
          </div>

          <footer>PingViewScan for Windows · A host counts as discovered if it answered ICMP or exposed a TCP port.</footer>
        </div>
        </body>
        </html>
        """;
    }

    private static string SubnetGrid(IReadOnlyList<GridCell> cells)
    {
        if (cells.Count == 0)
        {
            return "<svg viewBox=\"0 0 1020 80\"><text x=\"24\" y=\"40\" font-size=\"14\" fill=\"#6b7280\">No addresses scanned.</text></svg>";
        }

        int columns;
        if (cells.Count <= 16)
        {
            columns = cells.Count;
        }
        else if (cells.Count <= 256)
        {
            columns = 16;
        }
        else
        {
            columns = 32;
        }

        const double cellSize = 26;
        const double gap = 5;
        const double margin = 24;
        const double titleHeight = 34;
        var rows = (int)Math.Ceiling((double)cells.Count / columns);
        var width = margin * 2 + columns * (cellSize + gap) - gap;
        var height = titleHeight + margin + rows * (cellSize + gap) - gap + 44;

        var svg = new StringBuilder();
        svg.Append($"<svg viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\" width=\"100%\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" font-family=\"-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif\">");
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" fill=\"#ffffff\"/>");
        svg.Append($"<text x=\"{Fmt(margin)}\" y=\"24\" font-size=\"16\" font-weight=\"600\" fill=\"#111827\">Subnet occupancy</text>");
        svg.Append($"<text x=\"{Fmt(width - margin)}\" y=\"24\" font-size=\"12\" fill=\"#6b7280\" text-anchor=\"end\">{cells.Count} address{(cells.Count == 1 ? "" : "es")}</text>");

        for (var index = 0; index < cells.Count; index++)
        {
            var cell = cells[index];
            var row = index / columns;
            var column = index % columns;
            var x = margin + column * (cellSize + gap);
            var y = titleHeight + margin + row * (cellSize + gap);
            var fill = CellFill(cell);
            var tooltip = cell.Alive
                ? $"{cell.Ip} — {cell.PortCount} open port{(cell.PortCount == 1 ? "" : "s")}"
                : cell.Scanned ? $"{cell.Ip} — no response" : $"{cell.Ip} — not scanned";
            svg.Append($"<rect x=\"{Fmt(x)}\" y=\"{Fmt(y)}\" width=\"{Fmt(cellSize)}\" height=\"{Fmt(cellSize)}\" rx=\"6\" fill=\"{fill}\">");
            svg.Append($"<title>{Svg.Escape(tooltip)}</title></rect>");
        }

        var legendY = titleHeight + margin + rows * (cellSize + gap) + 8;
        var legend = new (string Color, string Label)[]
        {
            ("#86efac", "up (ICMP only)"),
            ("#22c55e", "up (1–2 ports)"),
            ("#15803d", "up (3+ ports)"),
            ("#e2e8f0", "no response"),
            ("#f1f5f9", "not scanned")
        };
        var legendX = margin;
        svg.Append("<g font-size=\"11\" fill=\"#6b7280\">");
        foreach (var (color, label) in legend)
        {
            svg.Append($"<rect x=\"{Fmt(legendX)}\" y=\"{Fmt(legendY - 9)}\" width=\"12\" height=\"12\" rx=\"3\" fill=\"{color}\"/>");
            svg.Append($"<text x=\"{Fmt(legendX + 17)}\" y=\"{Fmt(legendY)}\">{Svg.Escape(label)}</text>");
            legendX += 24 + label.Length * 7;
        }
        svg.Append("</g></svg>");
        return svg.ToString();
    }

    private static string CellFill(GridCell cell)
    {
        if (!cell.Alive)
        {
            return cell.Scanned ? "#e2e8f0" : "#f1f5f9";
        }
        return cell.PortCount switch
        {
            0 => "#86efac",
            <= 2 => "#22c55e",
            _ => "#15803d"
        };
    }

    private static string LatencyBars(IReadOnlyList<ScanResult> alive)
    {
        var responding = alive
            .Where(r => r.PingMs is not null)
            .Select(r => (r.Ip, Ms: r.PingMs!.Value))
            .OrderBy(r => IpToInt(r.Ip))
            .ToList();

        if (responding.Count == 0)
        {
            return Svg.BarChart(1020, 260, "Latency by host", Array.Empty<(string, double, string)>(), "Latency (ms)");
        }

        var shown = responding.Take(80).ToList();
        var bars = shown.Select(r => (r.Ip, r.Ms, "#2563eb")).ToList();
        var title = responding.Count > shown.Count
            ? $"Latency by host (first {shown.Count} of {responding.Count})"
            : "Latency by host";
        return Svg.BarChart(1020, 320, title, bars, "Latency (ms)", rotateLabels: shown.Count > 10);
    }

    private static string PortBars(IReadOnlyList<ScanResult> alive, IReadOnlyList<ushort> scannedPorts)
    {
        var counts = new Dictionary<ushort, int>();
        foreach (var result in alive)
        {
            foreach (var port in result.OpenPorts)
            {
                counts[port] = counts.GetValueOrDefault(port) + 1;
            }
        }

        if (counts.Count == 0)
        {
            var note = scannedPorts.Count == 0 ? "No ports were scanned." : "No open ports were found.";
            return Svg.BarChart(1020, 240, $"Open ports by host count ({note})", Array.Empty<(string, double, string)>(), "Hosts");
        }

        var bars = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(24)
            .Select(kv => (ServiceLabel(kv.Key), (double)kv.Value, "#7c3aed"))
            .ToList();
        return Svg.BarChart(1020, 320, "Most common open ports", bars, "Hosts", rotateLabels: true, integerValues: true);
    }

    private static string ServiceLabel(ushort port) => port switch
    {
        21 => "21 FTP", 22 => "22 SSH", 23 => "23 Telnet", 25 => "25 SMTP", 53 => "53 DNS",
        80 => "80 HTTP", 110 => "110 POP3", 123 => "123 NTP", 135 => "135 RPC", 139 => "139 NetBIOS",
        143 => "143 IMAP", 161 => "161 SNMP", 389 => "389 LDAP", 443 => "443 HTTPS", 445 => "445 SMB",
        554 => "554 RTSP", 587 => "587 SMTP", 636 => "636 LDAPS", 993 => "993 IMAPS", 995 => "995 POP3S",
        1433 => "1433 MSSQL", 1723 => "1723 PPTP", 3306 => "3306 MySQL", 3389 => "3389 RDP",
        5900 => "5900 VNC", 5985 => "5985 WinRM", 5986 => "5986 WinRM", 8080 => "8080 HTTP-alt",
        8443 => "8443 HTTPS-alt", 9100 => "9100 Print",
        _ => port.ToString(CultureInfo.InvariantCulture)
    };

    private static int IpToInt(string ip)
    {
        var value = 0;
        foreach (var part in ip.Split('.'))
        {
            value = value * 256 + (int.TryParse(part, out var n) ? n : 0);
        }
        return value;
    }

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
