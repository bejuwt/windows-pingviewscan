using System.Globalization;
using System.Text;

namespace PingViewScan.Core;

/// <summary>Result of computing an IPv4 subnet from an address and prefix length.</summary>
public sealed record SubnetInfo(
    string Address,
    int Prefix,
    string Network,
    string Broadcast,
    string Mask,
    string Wildcard,
    string? FirstUsable,
    string? LastUsable,
    ulong UsableHosts,
    ulong TotalAddresses,
    string IpClass,
    string Scope)
{
    public string Cidr => $"{Network}/{Prefix}";
    public string AddressBinary => SubnetCalculator.Binary(Address);
    public string MaskBinary => SubnetCalculator.Binary(Mask);
    public string UsableRange => FirstUsable is { } first && LastUsable is { } last ? $"{first} – {last}" : "—";

    /// <summary>Plain-text summary suitable for the clipboard.</summary>
    public string Summary
    {
        get
        {
            var lines = new List<string>
            {
                $"Address:        {Address}",
                $"Network:        {Cidr}",
                $"Subnet mask:    {Mask}",
                $"Wildcard mask:  {Wildcard}",
                $"Network addr:   {Network}",
                $"Broadcast:      {Broadcast}"
            };
            if (FirstUsable is not null && LastUsable is not null)
            {
                lines.Add($"Usable range:   {FirstUsable} - {LastUsable}");
            }
            lines.Add($"Usable hosts:   {UsableHosts}");
            lines.Add($"Total addrs:    {TotalAddresses}");
            lines.Add($"IP class:       {IpClass}");
            lines.Add($"Scope:          {Scope}");
            return string.Join("\n", lines);
        }
    }
}

/// <summary>Pure IPv4 subnet math, exposed for the Subnet Calculator tab.</summary>
public static class SubnetCalculator
{
    public static SubnetInfo? Compute(string address, int prefix)
    {
        if (IPRange.ParseIPv4(address) is not { } ip || prefix is < 0 or > 32)
        {
            return null;
        }

        var mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        var wildcard = ~mask;
        var network = ip & mask;
        var broadcast = network | wildcard;
        var total = 1UL << (32 - prefix);

        uint? firstUsable;
        uint? lastUsable;
        ulong usable;
        switch (prefix)
        {
            case 32:
                firstUsable = network;
                lastUsable = network;
                usable = 1;
                break;
            case 31:
                firstUsable = network;
                lastUsable = broadcast;
                usable = 2;
                break;
            default:
                firstUsable = network + 1;
                lastUsable = broadcast - 1;
                usable = total - 2;
                break;
        }

        return new SubnetInfo(
            IPRange.FormatIPv4(ip),
            prefix,
            IPRange.FormatIPv4(network),
            IPRange.FormatIPv4(broadcast),
            IPRange.FormatIPv4(mask),
            IPRange.FormatIPv4(wildcard),
            firstUsable is { } first ? IPRange.FormatIPv4(first) : null,
            lastUsable is { } last ? IPRange.FormatIPv4(last) : null,
            usable,
            total,
            IpClassOf(ip),
            ScopeOf(ip));
    }

    /// <summary>Dotted binary representation, e.g. <c>11000000.10101000.00000001.00001010</c>.</summary>
    public static string Binary(string dotted)
    {
        if (IPRange.ParseIPv4(dotted) is not { } value)
        {
            return string.Empty;
        }

        var bits = Convert.ToString(value, 2).PadLeft(32, '0');
        var builder = new StringBuilder(35);
        for (var index = 0; index < 32; index += 8)
        {
            if (index > 0)
            {
                builder.Append('.');
            }
            builder.Append(bits, index, 8);
        }
        return builder.ToString();
    }

    private static string IpClassOf(uint ip)
    {
        var first = (ip >> 24) & 0xFF;
        return first switch
        {
            <= 127 => "A",
            <= 191 => "B",
            <= 223 => "C",
            <= 239 => "D (multicast)",
            _ => "E (reserved)"
        };
    }

    private static string ScopeOf(uint ip)
    {
        var a = (ip >> 24) & 0xFF;
        var b = (ip >> 16) & 0xFF;
        var c = (ip >> 8) & 0xFF;

        if (a == 0) return "This network";
        if (a == 10) return "Private (RFC 1918)";
        if (a == 100 && b is >= 64 and <= 127) return "Shared / CGNAT (RFC 6598)";
        if (a == 127) return "Loopback";
        if (a == 169 && b == 254) return "Link-local (APIPA)";
        if (a == 172 && b is >= 16 and <= 31) return "Private (RFC 1918)";
        if (a == 192 && b == 168) return "Private (RFC 1918)";
        if (a == 192 && b == 0 && c == 2) return "Documentation (TEST-NET-1)";
        if (a == 198 && b == 51 && c == 100) return "Documentation (TEST-NET-2)";
        if (a == 203 && b == 0 && c == 113) return "Documentation (TEST-NET-3)";
        if (a is >= 224 and <= 239) return "Multicast";
        if (a >= 240) return "Reserved";
        return "Public";
    }

    /// <summary>Parses <c>address/prefix</c> or a bare address with a fallback prefix.</summary>
    public static (string Address, int Prefix) ParseInput(string input, int fallbackPrefix)
    {
        var trimmed = input.Trim();
        var parts = trimmed.Split('/');
        var address = parts[0].Trim();
        if (parts.Length > 1 &&
            int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefix))
        {
            return (address, Math.Clamp(prefix, 0, 32));
        }
        return (address, Math.Clamp(fallbackPrefix, 0, 32));
    }
}
