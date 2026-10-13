using System.Net;
using System.Net.Sockets;

namespace PingViewScan.Core;

/// <summary>DNS helpers for resolving hostnames and reverse (PTR) lookups.</summary>
public static class NetUtil
{
    public static async Task<string?> ResolveIpAsync(string host, CancellationToken ct = default)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal.ToString();
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            var v4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return (v4 ?? addresses.FirstOrDefault())?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> ReverseDnsAsync(string ip, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(ip, out _))
        {
            return null;
        }

        try
        {
            var entry = await Dns.GetHostEntryAsync(ip, ct).ConfigureAwait(false);
            var name = entry.HostName;
            return string.IsNullOrEmpty(name) || name == ip ? null : name;
        }
        catch
        {
            return null;
        }
    }
}
