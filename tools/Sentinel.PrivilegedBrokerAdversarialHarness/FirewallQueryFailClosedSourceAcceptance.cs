using System.Runtime.CompilerServices;

internal static class FirewallQueryFailClosedSourceAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string? root = FindRepoRoot();
        Require(root is not null, "Could not locate repository root for firewall query source acceptance.");

        string desktopPath = Path.Combine(root!, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "FirewallContainmentService.cs");
        string brokerPath = Path.Combine(root!, "src", "SentinelAI", "Sentinel.PrivilegedBroker", "Program.cs");
        Require(File.Exists(desktopPath), "Desktop firewall containment source was not found.");
        Require(File.Exists(brokerPath), "Broker firewall source was not found.");

        VerifyQuerySource(File.ReadAllText(desktopPath), "desktop");
        VerifyQuerySource(File.ReadAllText(brokerPath), "broker");

        string desktop = File.ReadAllText(desktopPath);
        Require(desktop.Contains("TryParseKeyValues(result.StandardOutput", StringComparison.Ordinal),
            "Desktop firewall query output is not pinned to strict parsing.");
        Require(desktop.Contains("values.ContainsKey(key)", StringComparison.Ordinal),
            "Desktop firewall parser does not reject duplicate evidence keys.");
    }

    private static void VerifyQuerySource(string source, string component)
    {
        Require(source.Contains("$ErrorActionPreference='Stop'; try {", StringComparison.Ordinal),
            $"{component} firewall query does not force terminating PowerShell errors.");
        Require(source.Contains("Get-NetFirewallRule -PolicyStore ActiveStore -DisplayName $name -ErrorAction Stop", StringComparison.Ordinal),
            $"{component} firewall rule lookup is not fail-closed.");
        Require(source.Contains("Get-NetFirewallAddressFilter -ErrorAction Stop", StringComparison.Ordinal),
            $"{component} firewall address-filter lookup is not fail-closed.");
        Require(source.Contains("Get-NetFirewallPortFilter -ErrorAction Stop", StringComparison.Ordinal),
            $"{component} firewall port-filter lookup is not fail-closed.");
        Require(source.Contains("Get-NetFirewallApplicationFilter -ErrorAction Stop", StringComparison.Ordinal),
            $"{component} firewall application-filter lookup is not fail-closed.");
        Require(source.Contains("Get-NetFirewallServiceFilter -ErrorAction Stop", StringComparison.Ordinal),
            $"{component} firewall service-filter lookup is not fail-closed.");
        Require(source.Contains("SENTINEL_FIREWALL_QUERY_FAILED", StringComparison.Ordinal) &&
                source.Contains("exit 70", StringComparison.Ordinal),
            $"{component} firewall query errors are not forced to a nonzero child-process result.");
        Require(!source.Contains("Get-NetFirewallRule -PolicyStore ActiveStore -DisplayName $name -ErrorAction SilentlyContinue", StringComparison.Ordinal),
            $"{component} firewall query still suppresses provider failures into apparent absence.");
    }

    private static string? FindRepoRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src", "SentinelAI")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
