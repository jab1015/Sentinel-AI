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
            $"name={BuildRuleName(normalized)}"
        };
    }

    private static string Normalize(string remoteIp)
    {
        if (!TryNormalizeRemoteIp(remoteIp, out string normalized))
            throw new ArgumentException("A literal IP address is required.", nameof(remoteIp));
        return normalized;
    }
}
