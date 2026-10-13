using System.Net.Sockets;

namespace PingViewScan.Core;

/// <summary>Minimal TCP connect scanner.</summary>
public static class PortScanner
{
    public static async Task<bool> IsOpenAsync(string host, int port, double timeoutSeconds, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
