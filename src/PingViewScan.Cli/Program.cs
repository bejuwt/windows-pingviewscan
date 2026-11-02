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

if (cmd.Has("trace"))
{
    try
    {
        await RunTraceAsync(cmd);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Traceroute failed: {ex.Message}");
        Environment.ExitCode = 1;
    }
    return;
}

if (cmd.Has("subnet"))
{
    RunSubnet(cmd);
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

  Traceroute:
    PingViewScan.Cli --trace 1.1.1.1 [--max-hops 30] [--queries 3]
                     [--trace-timeout 2] [--rounds 1] [--interval 5]
                     [--report out.html] [--csv out.csv]

  Subnet:
    PingViewScan.Cli --subnet 192.168.1.10/24 [--prefix 24]
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

static async Task RunTraceAsync(CommandLine cmd)
{
    var target = (cmd.Get("trace") ?? "").Trim();
    if (target.Length == 0 || target.StartsWith('-'))
    {
        Console.Error.WriteLine("A valid target host or IP is required.");
        Environment.ExitCode = 1;
        return;
    }

    var maxHops = Math.Max(1, cmd.GetInt("max-hops", 30));
    var queries = Math.Max(1, cmd.GetInt("queries", 3));
    var timeout = cmd.GetDouble("trace-timeout", 2);
    var rounds = Math.Max(1, cmd.GetInt("rounds", 1));
    var interval = cmd.GetDouble("interval", 5);

    Console.WriteLine($"Tracing route to {target} (max {maxHops} hops, {queries} probes/hop, timeout {timeout:g}s)");

    var started = DateTimeOffset.Now;
    var stats = new Dictionary<int, HopLatencyStats>();
    IReadOnlyList<TracerouteHop> lastHops = Array.Empty<TracerouteHop>();
    string? resolved = null;

    for (var round = 1; round <= rounds; round++)
    {
        if (rounds > 1)
        {
            Console.WriteLine($"Pass {round}/{rounds}");
        }

        var outcome = await TracerouteRunner.TraceAsync(
            target,
            queries,
            maxHops,
            timeout,
            ip =>
            {
                if (ip is not null)
                {
                    resolved = ip;
                }
            },
            hop =>
            {
                var rtt = hop.MinRtt is { } min ? $"{min,7:0.0} ms" : "       *";
                Console.WriteLine($"  {hop.Ttl,2}  {Pad(hop.DisplayHost, 34)} {Pad(hop.Ip ?? "-", 16)} {rtt}");
                if (!stats.TryGetValue(hop.Ttl, out var aggregate))
                {
                    aggregate = new HopLatencyStats(hop.Ttl);
                    stats[hop.Ttl] = aggregate;
                }
                aggregate.Merge(hop);
            });

        lastHops = outcome.Hops;
        resolved ??= outcome.ResolvedIP;

        if (lastHops.Count == 0 && !string.IsNullOrWhiteSpace(outcome.Output))
        {
            Console.Error.WriteLine(outcome.Output.Trim());
        }

        if (round < rounds)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, interval)));
        }
    }

    var finished = DateTimeOffset.Now;
    var report = new TracerouteReport(
        target,
        resolved,
        DateTimeOffset.Now,
        started,
        finished,
        maxHops,
        queries,
        timeout,
        lastHops,
        stats.Values.OrderBy(s => s.Ttl).ToList(),
        rounds);

    Console.WriteLine();
    Console.WriteLine($"{"Hop",3} {"Host",-34} {"IP",-16} {"Sent",5} {"Recv",5} {"Lost",5} {"Loss%",7} {"Min",8} {"Avg",8} {"Max",8}");
    foreach (var stat in report.StatsOrHops)
    {
        Console.WriteLine($"{stat.Ttl,3} {Pad(stat.DisplayHost, 34)} {Pad(stat.Ip ?? "-", 16)} {stat.Sent,5} {stat.Received,5} {stat.Timeouts,5} {stat.LossPercent,7:0.0} {Num(stat.MinRtt),8} {Num(stat.AvgRtt),8} {Num(stat.MaxRtt),8}");
    }
    Console.WriteLine(report.ReachedTarget
        ? $"Target {resolved} reached in {lastHops.Count} hop(s)."
        : $"Target not reached within {maxHops} hops.");

    if (cmd.Get("report") is { Length: > 0 } reportPath)
    {
        File.WriteAllText(reportPath, TracerouteReportExporter.Html(report));
        Console.WriteLine($"Wrote HTML report: {Path.GetFullPath(reportPath)}");
    }

    if (cmd.Get("csv") is { Length: > 0 } csvPath)
    {
        File.WriteAllText(csvPath, TracerouteReportExporter.Csv(report));
        Console.WriteLine($"Wrote CSV: {Path.GetFullPath(csvPath)}");
    }
}

static void RunSubnet(CommandLine cmd)
{
    var input = cmd.Get("subnet") ?? "";
    var (address, prefix) = SubnetCalculator.ParseInput(input, cmd.GetInt("prefix", 24));
    var info = SubnetCalculator.Compute(address, prefix);
    if (info is null)
    {
        Console.Error.WriteLine($"Invalid IPv4 address: {address}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine(info.Summary);
}

static string Pad(string value, int width) =>
    value.Length >= width ? value[..width] : value.PadRight(width);

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
