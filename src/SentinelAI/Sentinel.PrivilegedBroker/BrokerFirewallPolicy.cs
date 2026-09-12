using System.Net;
using System.Security.Cryptography;
using System.Text;

internal static class BrokerFirewallPolicy
{
    internal const string RulePrefix = "Sentinel AI Block";

    internal static bool TryNormalizeRemoteIp(string? value, out string remoteIp)
    {
        remoteIp = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!IPAddress.TryParse(value.Trim(), out IPAddress? address) || address is null) return false;
        remoteIp = address.ToString();
        return true;
    }

    internal static string BuildRuleName(string remoteIp)
    {
        if (!TryNormalizeRemoteIp(remoteIp, out string normalized))
            throw new ArgumentException("A literal IP address is required.", nameof(remoteIp));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"{RulePrefix} {Convert.ToHexString(hash)[..12]}";
    }

    internal static string[] BuildAddArguments(string remoteIp)
    {
        string normalized = Normalize(remoteIp);
        return new[]
        {
            "advfirewall", "firewall", "add", "rule",
            $"name={BuildRuleName(normalized)}",
            "dir=out",
            "action=block",
            $"remoteip={normalized}",
            "enable=yes",
            "profile=any"
        };
    }

    internal static string[] BuildDeleteArguments(string remoteIp)
    {
        string normalized = Normalize(remoteIp);
        return new[]
        {
            "advfirewall", "firewall", "delete", "rule",
            $"name={BuildRuleName(normalized)}",
            "dir=out",
            $"remoteip={normalized}"
        };
    }

    internal static BrokerFirewallRuleVerification EvaluateRemovalEvidence(string? output, string remoteIp)
    {
        if (!TryNormalizeRemoteIp(remoteIp, out string normalized) ||
            !IPAddress.TryParse(normalized, out IPAddress? expectedAddress) || expectedAddress is null)
            return new(false, false, false, "expected remote IP is invalid");

        if (!TryParseKeyValues(output, out Dictionary<string, string> values))
            return new(false, false, false, "firewall query output was malformed or ambiguous");

        if (!values.TryGetValue("FOUND", out string? foundText) ||
            !int.TryParse(foundText, out int found) || found < 0)
            return new(false, false, false, "firewall query output did not contain a valid FOUND count");

        if (found == 0)
            return new(true, false, false, "rule not found");

        if (found != 1 || values.ContainsKey("CONFLICT"))
            return new(true, true, false, "more than one rule has the deterministic Sentinel name");

        bool enabled = IsExactly(values, "ENABLED", "True");
        bool block = IsExactly(values, "ACTION", "Block");
        bool outbound = IsOneOf(values, "DIRECTION", "Outbound", "Out");
        bool profileAny = IsExactly(values, "PROFILE", "Any");
        bool remoteExact = values.TryGetValue("REMOTE", out string? remote) &&
                           AddressListExactlyMatches(remote, expectedAddress);
        bool localAny = IsAny(values, "LOCAL");
        bool protocolAny = IsAny(values, "PROTOCOL");
        bool localPortAny = IsAny(values, "LOCALPORT");
        bool remotePortAny = IsAny(values, "REMOTEPORT");
        bool programAny = IsAny(values, "PROGRAM");
        bool serviceAny = IsAny(values, "SERVICE");

        bool exact = enabled && block && outbound && profileAny && remoteExact && localAny &&
                     protocolAny && localPortAny && remotePortAny && programAny && serviceAny;

        string detail = exact
            ? "exact enabled outbound Block rule verified inside broker"
            : $"Enabled={enabled}; ActionBlock={block}; Outbound={outbound}; ProfileAny={profileAny}; RemoteExact={remoteExact}; LocalAny={localAny}; ProgramAny={programAny}; ServiceAny={serviceAny}; ProtocolAny={protocolAny}; PortsAny={localPortAny && remotePortAny}";

        return new(true, true, exact, detail);
    }

    private static bool TryParseKeyValues(string? output, out Dictionary<string, string> values)
    {
        values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in (output ?? string.Empty).Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = line.IndexOf('=');
            if (separator <= 0) continue;
            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || values.ContainsKey(key)) return false;
            values.Add(key, value);
        }
        return true;
    }

    private static bool IsExactly(IReadOnlyDictionary<string, string> values, string key, string expected) =>
        values.TryGetValue(key, out string? value) &&
        value.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsOneOf(
        IReadOnlyDictionary<string, string> values,
        string key,
        params string[] expectedValues)
    {
        if (!values.TryGetValue(key, out string? value)) return false;
        foreach (string expected in expectedValues)
        {
            if (value.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsAny(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) &&
        !string.IsNullOrWhiteSpace(value) &&
        (value.Equals("Any", StringComparison.OrdinalIgnoreCase) || value.Equals("*", StringComparison.OrdinalIgnoreCase));

    private static bool AddressListExactlyMatches(string value, IPAddress expected)
    {
        string[] items = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Length == 1 &&
               IPAddress.TryParse(items[0], out IPAddress? parsed) &&
               parsed.Equals(expected);
    }

    private static string Normalize(string remoteIp)
    {
        if (!TryNormalizeRemoteIp(remoteIp, out string normalized))
            throw new ArgumentException("A literal IP address is required.", nameof(remoteIp));
        return normalized;
    }
}

internal sealed record BrokerFirewallRuleVerification(
    bool QueryValid,
    bool Exists,
    bool IsExactBlock,
    string Detail);
