using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PingViewScan.Core;

public readonly record struct PingResult(double? RttMs, string Output)
{
    public bool Success => RttMs is not null;
}

/// <summary>
/// Runs a single ICMP echo by invoking the operating system's <c>ping</c> tool,
/// which works without elevated privileges on both Windows and Unix. The host
/// is always passed as a dedicated argv entry (never through a shell), and any
/// value beginning with <c>-</c> is rejected to avoid option injection.
/// </summary>
public static class PingRunner
{
    private static readonly Regex RttRegex =
        new(@"[=<]\s*([0-9]+(?:\.[0-9]+)?)\s*ms", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<PingResult> PingAsync(string host, double timeoutSeconds, CancellationToken ct = default)
    {
        var target = host.Trim();
        if (target.Length == 0 || target.StartsWith('-'))
        {
            return new PingResult(null, $"Refused invalid host: {host}");
        }

        var (fileName, arguments) = BuildCommand(target, timeoutSeconds);
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new PingResult(null, "failed to start ping");
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return new PingResult(ParseRtt(output), output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PingResult(null, ex.Message);
        }
    }

    private static (string FileName, string[] Arguments) BuildCommand(string target, double timeoutSeconds)
    {
        var waitMs = Math.Max(100, (int)Math.Round(timeoutSeconds * 1000.0));
        var wait = waitMs.ToString(CultureInfo.InvariantCulture);

        if (OperatingSystem.IsWindows())
        {
            var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
            return (ping, new[] { "-n", "1", "-w", wait, target });
        }

        var unixPing = File.Exists("/sbin/ping") ? "/sbin/ping" : "/bin/ping";
        return (unixPing, new[] { "-c", "1", "-n", "-W", wait, target });
    }

    private static double? ParseRtt(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }
        var match = RttRegex.Match(output);
        if (!match.Success)
        {
            return null;
        }
        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
