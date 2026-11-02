using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PingViewScan.Core;

/// <summary>Final result of a traceroute invocation.</summary>
public sealed record TracerouteOutcome(
    IReadOnlyList<TracerouteHop> Hops,
    string? ResolvedIP,
    bool ReachedTarget,
    string Output);

/// <summary>
/// Drives the operating system's traceroute tool (<c>tracert</c> on Windows,
/// <c>traceroute</c> elsewhere) and parses its output into hops as they arrive,
/// so the UI can update live.
/// </summary>
public static class TracerouteRunner
{
    private sealed record CommandSpec(string FileName, string[] Arguments);

    /// <summary>
    /// Runs a traceroute. <paramref name="onHeader"/> fires once the resolved
    /// target IP is known; <paramref name="onHop"/> fires as each hop completes.
    /// Callbacks run on a background thread. Always call off the UI thread.
    /// </summary>
    public static async Task<TracerouteOutcome> TraceAsync(
        string rawTarget,
        int queries = 3,
        int maxHops = 30,
        double timeout = 2,
        Action<string?>? onHeader = null,
        Action<TracerouteHop>? onHop = null,
        CancellationToken ct = default)
    {
        // Guard against argument injection: a leading "-" would be parsed by
        // traceroute's option parser.
        var target = (rawTarget ?? string.Empty).Trim();
        if (target.Length == 0 || target.StartsWith('-'))
        {
            onHeader?.Invoke(null);
            return new TracerouteOutcome(Array.Empty<TracerouteHop>(), null, false,
                $"Refused invalid target: {rawTarget}");
        }

        var spec = BuildCommand(target, queries, maxHops, timeout);
        var psi = new ProcessStartInfo(spec.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in spec.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                onHeader?.Invoke(null);
                return new TracerouteOutcome(Array.Empty<TracerouteHop>(), null, false,
                    "Failed to launch traceroute.");
            }
        }
        catch (Exception ex)
        {
            onHeader?.Invoke(null);
            return new TracerouteOutcome(Array.Empty<TracerouteHop>(), null, false,
                $"Failed to launch traceroute: {ex.Message}");
        }

        using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process already exited.
            }
        });

        var parser = new TracerouteParser(target);
        var output = new StringBuilder();
        var gate = new object();

        if (parser.ResolvedIP is not null)
        {
            onHeader?.Invoke(parser.ResolvedIP);
        }

        void HandleLine(string line)
        {
            lock (gate)
            {
                output.AppendLine(line);
                var headerBefore = parser.ResolvedIP;
                var completed = parser.Consume(line);
                if (completed is not null)
                {
                    onHop?.Invoke(completed);
                }
                if (parser.ResolvedIP != headerBefore)
                {
                    onHeader?.Invoke(parser.ResolvedIP);
                }
            }
        }

        var stdout = ReadLinesAsync(process.StandardOutput, HandleLine);
        var stderr = ReadLinesAsync(process.StandardError, HandleLine);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        var final = parser.Finish();
        if (final is not null)
        {
            onHop?.Invoke(final);
        }
        onHeader?.Invoke(parser.ResolvedIP);

        var reached = parser.ResolvedIP is { } ip &&
                      parser.Hops.Any(h => h.Ip == ip && !h.IsTimeout);

        return new TracerouteOutcome(parser.Hops, parser.ResolvedIP, reached, output.ToString());
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            onLine(line);
        }
    }

    private static CommandSpec BuildCommand(string target, int queries, int maxHops, double timeout)
    {
        queries = Math.Max(1, queries);
        maxHops = Math.Max(1, maxHops);

        if (OperatingSystem.IsWindows())
        {
            var tracert = Path.Combine(Environment.SystemDirectory, "tracert.exe");
            var waitMs = Math.Max(100, (int)Math.Round(timeout * 1000.0)).ToString(CultureInfo.InvariantCulture);
            return new CommandSpec(tracert, new[]
            {
                "-h", maxHops.ToString(CultureInfo.InvariantCulture),
                "-w", waitMs,
                target
            });
        }

        var traceroute = FindExecutable("traceroute",
            "/usr/sbin/traceroute", "/usr/bin/traceroute", "/sbin/traceroute", "/bin/traceroute");
        if (traceroute is null)
        {
            throw new FileNotFoundException(
                "Could not find the traceroute executable. Install traceroute or run on Windows.");
        }

        var waitSeconds = Math.Max(0.1, timeout).ToString("0.##", CultureInfo.InvariantCulture);
        return new CommandSpec(traceroute, new[]
        {
            "-m", maxHops.ToString(CultureInfo.InvariantCulture),
            "-w", waitSeconds,
            "-q", queries.ToString(CultureInfo.InvariantCulture),
            target
        });
    }

    private static string? FindExecutable(string name, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }
}

/// <summary>
/// Incremental parser for both Windows <c>tracert</c> and BSD/Linux
/// <c>traceroute</c> output. Feed it one line at a time; it returns a completed
/// hop whenever a new hop begins.
/// </summary>
public sealed class TracerouteParser
{
    public TracerouteParser(string? knownTarget = null)
    {
        if (knownTarget is not null && IPAddress.TryParse(knownTarget, out var literal))
        {
            ResolvedIP = literal.ToString();
        }
    }

    private static readonly Regex BsdHeaderRegex = new(
        @"traceroute6?\s+to\s+\S+\s+\(([0-9a-fA-F:.]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex WindowsHeaderRegex = new(
        @"Tracing route to \S+ \[([0-9a-fA-F:.]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex WindowsLiteralHeaderRegex = new(
        @"Tracing route to ([0-9a-fA-F:.]+) over",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex HopStartRegex = new(
        @"^\s*(\d+)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string? ResolvedIP { get; private set; }

    public List<TracerouteHop> Hops { get; } = new();

    /// <summary>One word per line - consume a line of traceroute output.</summary>
    public TracerouteHop? Consume(string line)
    {
        var text = line.TrimEnd('\r');

        if (ResolvedIP is null)
        {
            var header = ParseHeader(text);
            if (header is not null)
            {
                ResolvedIP = header;
                return null;
            }
        }

        var split = HopStartRegex.Match(text);
        if (split.Success &&
            int.TryParse(split.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ttl) &&
            ttl == (Hops.Count > 0 ? Hops[^1].Ttl + 1 : 1))
        {
            // A new hop begins: the previous one is now complete.
            var completed = Hops.Count > 0 ? Hops[^1] : null;
            var hop = new TracerouteHop(ttl);
            Absorb(split.Groups[2].Value, hop);
            Hops.Add(hop);
            return completed;
        }

        if (Hops.Count > 0)
        {
            Absorb(text, Hops[^1]);
        }
        return null;
    }

    /// <summary>Finalise the trailing in-progress hop after the process exits.</summary>
    public TracerouteHop? Finish() => Hops.Count > 0 ? Hops[^1] : null;

    private static string? ParseHeader(string line)
    {
        var match = BsdHeaderRegex.Match(line);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = WindowsHeaderRegex.Match(line);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = WindowsLiteralHeaderRegex.Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Parse a hop's content into probe results. Handles the variants:
    ///   "*"                                      → timeout
    ///   "name (1.2.3.4)  12.3 ms  11.1 ms"       → named router, several probes
    ///   "1.2.3.4  12.3 ms"                       → unnamed router
    ///   "1 ms  2 ms  3 ms  192.168.1.1"          → Windows format
    /// </summary>
    private static void Absorb(string content, TracerouteHop hop)
    {
        var tokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? pendingName = null;

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];

            if (token == "*")
            {
                hop.Sent++;
                hop.Timeouts++;
                continue;
            }

            // Windows/localised timeout text. Everything after it is prose, not identity.
            if (token is "Request" or "Destination" or "Transmit" or "The" or "Trace")
            {
                break;
            }

            // Linux ICMP unreachable annotations such as !H, !N, !X.
            if (token.StartsWith('!'))
            {
                continue;
            }

            // Parenthesised or bracketed IP that follows a name: "name (1.2.3.4)".
            if ((token.StartsWith('(') && token.EndsWith(')')) ||
                (token.StartsWith('[') && token.EndsWith(']')))
            {
                var ip = token[1..^1];
                if (IPAddress.TryParse(ip, out _))
                {
                    hop.Ip ??= ip;
                    if (hop.Host is null && pendingName is not null && !IsIPAddress(pendingName))
                    {
                        hop.Host = pendingName;
                    }
                }
                pendingName = null;
                continue;
            }

            // "<number> ms" → a probe round-trip time. Windows writes "<1 ms".
            if (index + 1 < tokens.Length && tokens[index + 1] == "ms")
            {
                if (TryParseRtt(token, out var rtt))
                {
                    hop.Sent++;
                    hop.Rtts.Add(rtt);
                    if (pendingName is not null)
                    {
                        if (hop.Ip is null && IsIPAddress(pendingName))
                        {
                            hop.Ip = pendingName;
                        }
                        if (hop.Host is null && !IsIPAddress(pendingName))
                        {
                            hop.Host = pendingName;
                        }
                    }
                    pendingName = null;
                    index++;
                    continue;
                }
            }

            if (token == "ms")
            {
                continue;
            }

            pendingName = token;
        }

        if (pendingName is not null)
        {
            if (IsIPAddress(pendingName))
            {
                hop.Ip ??= pendingName;
            }
            else if (hop.Host is null)
            {
                hop.Host = pendingName;
            }
        }
    }

    private static bool TryParseRtt(string token, out double value)
    {
        var text = token;
        if (text.StartsWith('<') || text.StartsWith('='))
        {
            text = text[1..];
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }
        // "<1 ms" is a sub-millisecond response; 0.5 keeps it on the chart.
        if (token.StartsWith('<') && value <= 1)
        {
            value = 0.5;
        }
        return true;
    }

    private static bool IsIPAddress(string value) => IPAddress.TryParse(value, out _);
}
