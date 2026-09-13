internal static class BrokerProcessTerminationPolicy
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "svchost", "dwm", "explorer", "Sentinel.App",
        "Sentinel.PrivilegedBroker", "Memory Compression", "Secure System", "LsaIso",
        "fontdrvhost", "sihost", "taskhostw", "conhost", "WmiPrvSE", "MsMpEng",
        "SecurityHealthService", "NisSrv"
    };

    internal static bool IsProtected(string? processName)
    {
        string normalized = Normalize(processName);
        return string.IsNullOrWhiteSpace(normalized) || ProtectedProcessNames.Contains(normalized);
    }

    internal static string Normalize(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
        string trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
