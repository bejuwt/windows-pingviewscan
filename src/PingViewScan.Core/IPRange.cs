using System.Globalization;

namespace PingViewScan.Core;

/// <summary>
/// Expands an IP block expression into a list of addresses. Supports single
/// addresses, CIDR (<c>10.0.0.0/24</c>), ranges (<c>10.0.0.1-10.0.0.20</c> or
/// <c>10.0.0.1-20</c>) and wildcards (<c>10.0.0.*</c> or <c>10.0.0.x</c>).
/// </summary>
public static class IPRange
{
    public const int DefaultLimit = 65536;

    public static List<string> Expand(string input, int limit = DefaultLimit)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var separators = new[] { ',', ';', ' ', '\t', '\n', '\r' };
        foreach (var raw in input.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ip in ExpandToken(raw.Trim(), limit))
            {
                result.Add(ip);
                if (result.Count >= limit)
                {
                    return result;
                }
            }
        }
        return result;
    }

    private static List<string> ExpandToken(string token, int limit)
    {
        if (token.Length == 0)
        {
            return new List<string>();
        }
        if (token.Contains('/'))
        {
            return Cidr(token, limit);
        }
        if (token.Contains('*') || token.Contains('x') || token.Contains('X'))
        {
            return Wildcard(token, limit);
        }
        if (token.Contains('-'))
        {
            return Range(token, limit);
        }
        return new List<string> { token };
    }

    private static List<string> Cidr(string token, int limit)
    {
        var list = new List<string>();
        var parts = token.Split('/');
        if (parts.Length != 2)
        {
            return list;
        }
        if (ParseIPv4(parts[0]) is not { } baseIp)
        {
            return list;
        }
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefix) ||
            prefix is < 0 or > 32)
        {
            return list;
        }

        var mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        var network = baseIp & mask;
        var broadcast = network | ~mask;
        var start = prefix <= 30 ? network + 1 : network;
        var end = prefix <= 30 ? broadcast - 1 : broadcast;

        for (var ip = start; ; ip++)
        {
            list.Add(FormatIPv4(ip));
            if (ip == end || list.Count >= limit)
            {
                break;
            }
        }
        return list;
    }

    private static List<string> Wildcard(string token, int limit)
    {
        var list = new List<string>();
        var parts = token.Split('.');
        if (parts.Length != 4)
        {
            return list;
        }

        var options = new List<int>[4];
        for (var i = 0; i < 4; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || part == "*" || part.Equals("x", StringComparison.OrdinalIgnoreCase))
            {
                options[i] = Enumerable.Range(0, 256).ToList();
            }
            else if (int.TryParse(part, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 255)
            {
                options[i] = new List<int> { value };
            }
            else
            {
                return list;
            }
        }

        foreach (var a in options[0])
        foreach (var b in options[1])
        foreach (var c in options[2])
        foreach (var d in options[3])
        {
            list.Add($"{a}.{b}.{c}.{d}");
            if (list.Count >= limit)
            {
                return list;
            }
        }
        return list;
    }

    private static List<string> Range(string token, int limit)
    {
        var list = new List<string>();
        var parts = token.Split('-');
        if (parts.Length != 2)
        {
            return list;
        }

        var startIp = ParseIPv4(parts[0].Trim());
        var endIp = ParseIPv4(parts[1].Trim());

        if (startIp is { } s && endIp is { } e && s <= e)
        {
            for (var ip = s; ; ip++)
            {
                list.Add(FormatIPv4(ip));
                if (ip == e || list.Count >= limit)
                {
                    break;
                }
            }
            return list;
        }

        // Last-octet shorthand, e.g. 10.0.0.1-20
        if (startIp is { } s2 &&
            int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastOctet) &&
            lastOctet is >= 0 and <= 255)
        {
            var network = s2 & 0xFFFFFF00u;
            var start = network | (s2 & 0xFFu);
            var end = network | (uint)lastOctet;
            if (start <= end)
            {
                for (var ip = start; ; ip++)
                {
                    list.Add(FormatIPv4(ip));
                    if (ip == end || list.Count >= limit)
                    {
                        break;
                    }
                }
            }
        }
        return list;
    }

    public static uint? ParseIPv4(string value)
    {
        var parts = value.Trim().Split('.');
        if (parts.Length != 4)
        {
            return null;
        }
        uint result = 0;
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 3 ||
                !uint.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var octet) ||
                octet > 255)
            {
                return null;
            }
            result = (result << 8) | octet;
        }
        return result;
    }

    public static string FormatIPv4(uint value) =>
        $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
}
