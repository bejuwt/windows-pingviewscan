namespace PingViewScan.Core;

/// <summary>
/// Parses pasted/typed host lists (from Excel, CSV, or free text). Splits on
/// tabs, newlines, commas, semicolons and spaces, strips surrounding quotes, and
/// optionally filters out values that are not valid IP addresses or hostnames.
/// </summary>
public static class HostImport
{
    private static readonly char[] Separators = { '\t', '\n', '\r', ',', ';', ' ' };

    public static List<string> Parse(string raw, bool filterInvalid)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
        {
            return result;
        }

        foreach (var piece in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = Clean(piece);
            if (token.Length == 0)
            {
                continue;
            }
            if (filterInvalid && !IsValidHost(token))
            {
                continue;
            }
            if (seen.Add(token))
            {
                result.Add(token);
            }
        }
        return result;
    }

    public static bool IsValidHost(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        if (value.Contains(':'))
        {
            return System.Net.IPAddress.TryParse(value, out _);
        }
        if (IPRange.ParseIPv4(value) is not null)
        {
            return true;
        }
        return IsPlausibleHostname(value);
    }

    private static bool IsPlausibleHostname(string value)
    {
        if (value.Length > 253)
        {
            return false;
        }
        // Reject digit/dot-only strings that are not valid IPs.
        if (value.All(c => char.IsDigit(c) || c == '.'))
        {
            return false;
        }
        // A label-less name is only accepted if it contains a dot.
        if (!value.Contains('.') && !value.Any(char.IsDigit))
        {
            return false;
        }

        foreach (var label in value.Split('.'))
        {
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }
            foreach (var ch in label)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-')
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static string Clean(string value)
    {
        var token = value.Trim();
        if (token.Length >= 2)
        {
            var first = token[0];
            var last = token[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                token = token[1..^1].Trim();
            }
        }
        return token;
    }
}
