namespace PingViewScan.Core;

public sealed record ScanOptions(
    string Targets,
    IReadOnlyList<ushort> Ports,
    double PingTimeout,
    double PortTimeout,
    int Concurrency);

/// <summary>
/// Drives an IP-block scan: expands the target expression, pings each address,
/// scans the requested TCP ports, resolves reverse DNS for live hosts, and
/// reports each finished address as it completes.
/// </summary>
public static class ScanRunner
{
    public static async Task<List<ScanResult>> RunAsync(
        ScanOptions options,
        IProgress<ScanResult>? progress = null,
        CancellationToken ct = default)
    {
        var results = IPRange.Expand(options.Targets).Select(ip => new ScanResult(ip)).ToList();
        using var semaphore = new SemaphoreSlim(Math.Max(1, options.Concurrency));

        var tasks = results.Select(async result =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                var ping = await PingRunner.PingAsync(result.Ip, options.PingTimeout, ct).ConfigureAwait(false);

                var openPorts = new List<ushort>();
                if (options.Ports.Count > 0)
                {
                    var probes = options.Ports.Select(async port =>
                        (Port: port, Open: await PortScanner.IsOpenAsync(result.Ip, port, options.PortTimeout, ct)
                            .ConfigureAwait(false)));
                    var outcomes = await Task.WhenAll(probes).ConfigureAwait(false);
                    openPorts = outcomes.Where(o => o.Open).Select(o => o.Port).OrderBy(p => p).ToList();
                }

                result.PingMs = ping.RttMs;
                result.OpenPorts = openPorts;
                result.Hostname = result.IsAlive
                    ? await NetUtil.ReverseDnsAsync(result.Ip, ct).ConfigureAwait(false)
                    : null;
                result.Scanned = true;
                progress?.Report(result);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
