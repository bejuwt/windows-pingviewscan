using System.Globalization;
using PingViewScan.Core;

var cmd = CommandLine.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());

if (cmd.Has("scan"))
{
    await RunScanAsync(cmd);
    return;
}

if (cmd.Has("hosts"))
{
    await RunMonitorAsync(cmd);
    return;
}

Console.Error.WriteLine("""
PingViewScan CLI

  Monitor:
    PingViewScan.Cli --hosts 10.0.0.1,10.0.0.2 [--duration 10] [--interval 1]
                     [--timeout 1] [--report out.html] [--csv out.csv]

  Scan:
    PingViewScan.Cli --scan 10.0.0.0/24 [--ports 22,80,443,8000-8010]
                     [--ping-timeout 1] [--port-timeout 1] [--concurrency 64]
                     [--report out.html] [--csv out.csv]
""");
Environment.ExitCode = 1;
return;

static async Task RunMonitorAsync(CommandLine cmd)
{
    var hosts = HostImport.Parse(cmd.Get("hosts") ?? "", filterInvalid: false);
    var duration = cmd.GetDouble("duration", 10);
    var interval = cmd.GetDouble("interval", 1);
    var timeout = cmd.GetDouble("timeout", 1);

    var tracked = hosts.Select(h => new Host(h)).ToList();
    var started = DateTimeOffset.Now;

    Console.WriteLine($"Monitoring {tracked.Count} host(s) for {duration:g}s (interval {interval:g}s, timeout {timeout:g}s)");

    foreach (var host in tracked)
    {
        host.Ip = await NetUtil.ResolveIpAsync(host.Address);
        if (host.Ip is not null)
        {
            host.Hostname = await NetUtil.ReverseDnsAsync(host.Ip);
        }
    }

    var deadline = started.AddSeconds(duration);
    while (DateTimeOffset.Now < deadline)
    {
        var probes = tracked.Select(async host =>
        {
            var result = await PingRunner.PingAsync(host.Address, timeout);
            return (host, result);
        });
        var outcomes = await Task.WhenAll(probes);
        foreach (var (host, result) in outcomes)
        {
            host.Stats.Add(result.RttMs);
            host.Samples.Add(new PingSample(host.Samples.Count + 1, DateTimeOffset.Now, result.RttMs));
        }
        await Task.Delay(TimeSpan.FromSeconds(interval));
    }

    var ended = DateTimeOffset.Now;
    var report = new SessionReport(
        ended,
        started,
        ended,
        interval,
        timeout,
        tracked.Select(h => new HostReport(h.Address, h.Ip, h.Stats, h.Samples)).ToList());

    Console.WriteLine($"{"Host",-20} {"IP",-16} {"Sent",5} {"Recv",5} {"Lost",5} {"Loss%",7} {"Last",8} {"Min",8} {"Max",8} {"Avg",8}");
    foreach (var host in tracked)
    {
        var s = host.Stats;
        Console.WriteLine($"{host.Address,-20} {host.Ip ?? "-",-16} {s.Sent,5} {s.Received,5} {s.Lost,5} {s.LossPercent,7:0.0} {Num(s.Last),8} {Num(s.Min),8} {Num(s.Max),8} {Num(s.Average),8}");
    }

    WriteOutputs(cmd, report, null);
}

static async Task RunScanAsync(CommandLine cmd)
{
    var targets = cmd.Get("scan") ?? "";
    var ports = PortList.Parse(cmd.Get("ports") ?? "");
    var pingTimeout = cmd.GetDouble("ping-timeout", 1);
    var portTimeout = cmd.GetDouble("port-timeout", 1);
    var concurrency = cmd.GetInt("concurrency", 64);

    var options = new ScanOptions(targets, ports, pingTimeout, portTimeout, concurrency);
    var started = DateTimeOffset.Now;

    var progress = new Progress<ScanResult>(_ => { });
    var results = await ScanRunner.RunAsync(options, progress);
    var finished = DateTimeOffset.Now;

    var report = new ScanReport(
        finished, targets, started, finished, ports, results, pingTimeout, portTimeout, concurrency);

    var alive = report.AliveCount;
    Console.WriteLine($"{results.Count} address(es) scanned, {alive} discovered, {results.Sum(r => r.OpenPorts.Count)} open port(s) in {report.Duration.TotalSeconds:0.0}s");
    Console.WriteLine($"{"IP",-16} {"Hostname",-24} {"Status",8} {"Ping",8} {"Open ports"}");
    foreach (var r in results)
    {
        var status = r.IsAlive ? "up" : "-";
        var ping = r.PingMs is { } ms ? ms.ToString("0.0", CultureInfo.InvariantCulture) : "-";
        var open = r.OpenPorts.Count == 0 ? "-" : string.Join(",", r.OpenPorts);
        Console.WriteLine($"{r.Ip,-16} {r.Hostname ?? "-",-24} {status,8} {ping,8} {open}");
    }

    WriteOutputs(cmd, null, report);
}

static void WriteOutputs(CommandLine cmd, SessionReport? monitor, ScanReport? scan)
{
    if (cmd.Get("report") is { } reportPath && reportPath.Length > 0)
    {
        var html = scan is not null ? ScanReportExporter.Html(scan) : ReportExporter.Html(monitor!);
        File.WriteAllText(reportPath, html);
        Console.WriteLine($"Wrote HTML report: {Path.GetFullPath(reportPath)}");
    }

    if (cmd.Get("csv") is { } csvPath && csvPath.Length > 0)
    {
        var csv = scan is not null ? ScanCsv(scan) : ReportExporter.Csv(monitor!);
        File.WriteAllText(csvPath, csv);
        Console.WriteLine($"Wrote CSV: {Path.GetFullPath(csvPath)}");
    }
}

static string ScanCsv(ScanReport report)
{
    var lines = new List<string> { "IP,Hostname,Alive,Ping_ms,Open_Ports" };
    foreach (var r in report.Results)
    {
        var ping = r.PingMs is { } ms ? ms.ToString("0.0", CultureInfo.InvariantCulture) : "";
        var ports = string.Join(" ", r.OpenPorts);
        lines.Add($"{Csv(r.Ip)},{Csv(r.Hostname ?? "")},{(r.IsAlive ? "1" : "0")},{ping},{ports}");
    }
    return string.Join("\n", lines) + "\n";
}

static string Csv(string value)
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

static string Num(double? value) => value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "-";

internal sealed class CommandLine
{
    private readonly Dictionary<string, string> _values;

    private CommandLine(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static CommandLine Parse(string[] args0)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args0.Length; i++)
        {
            var token = args0[i];
            if (!token.StartsWith("--"))
            {
                continue;
            }
            var key = token[2..];
            if (i + 1 < args0.Length && !args0[i + 1].StartsWith("--"))
            {
                values[key] = args0[++i];
            }
            else
            {
                values[key] = "1";
            }
        }
        return new CommandLine(values);
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public double GetDouble(string key, double fallback) =>
        Get(key) is { } value && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public int GetInt(string key, int fallback) =>
        Get(key) is { } value && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
