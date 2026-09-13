using System.Runtime.CompilerServices;

internal static class BrokerFirewallRemovalAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        const string remoteIp = "203.0.113.10";
        string exact = Evidence(remoteIp);

        BrokerFirewallRuleVerification accepted = BrokerFirewallPolicy.EvaluateRemovalEvidence(exact, remoteIp);
        Require(accepted.QueryValid && accepted.Exists && accepted.IsExactBlock,
            "Broker did not accept the exact enabled outbound Block rule before mutation.");

        BrokerFirewallRuleVerification absent = BrokerFirewallPolicy.EvaluateRemovalEvidence("FOUND=0", remoteIp);
        Require(absent.QueryValid && !absent.Exists && !absent.IsExactBlock,
            "Broker did not distinguish verified rule absence from invalid evidence.");

        BrokerFirewallRuleVerification allow = BrokerFirewallPolicy.EvaluateRemovalEvidence(
            exact.Replace("ACTION=Block", "ACTION=Allow", StringComparison.Ordinal), remoteIp);
        Require(allow.QueryValid && allow.Exists && !allow.IsExactBlock,
            "Broker would accept an Allow rule as exact Sentinel firewall state.");

        BrokerFirewallRuleVerification disabled = BrokerFirewallPolicy.EvaluateRemovalEvidence(
            exact.Replace("ENABLED=True", "ENABLED=False", StringComparison.Ordinal), remoteIp);
        Require(disabled.QueryValid && disabled.Exists && !disabled.IsExactBlock,
            "Broker would accept a disabled rule as exact Sentinel firewall state.");

        BrokerFirewallRuleVerification broadRemote = BrokerFirewallPolicy.EvaluateRemovalEvidence(
            exact.Replace($"REMOTE={remoteIp}", $"REMOTE={remoteIp},203.0.113.11", StringComparison.Ordinal), remoteIp);
        Require(broadRemote.QueryValid && broadRemote.Exists && !broadRemote.IsExactBlock,
            "Broker would accept broader remote-address scope as exact Sentinel firewall state.");

        BrokerFirewallRuleVerification duplicateNamedRules = BrokerFirewallPolicy.EvaluateRemovalEvidence(
            "FOUND=2\nCONFLICT=True", remoteIp);
        Require(duplicateNamedRules.QueryValid && duplicateNamedRules.Exists && !duplicateNamedRules.IsExactBlock,
            "Broker did not fail closed on duplicate deterministic firewall rules.");

        BrokerFirewallRuleVerification duplicateEvidenceKey = BrokerFirewallPolicy.EvaluateRemovalEvidence(
            exact + "\nACTION=Allow", remoteIp);
        Require(!duplicateEvidenceKey.QueryValid,
            "Broker accepted ambiguous duplicate firewall evidence keys.");

        BrokerFirewallRuleVerification malformed = BrokerFirewallPolicy.EvaluateRemovalEvidence(string.Empty, remoteIp);
        Require(!malformed.QueryValid,
            "Broker treated malformed firewall evidence as verified absence.");

        string? brokerProgram = FindBrokerProgram();
        Require(brokerProgram is not null,
            "Could not locate Sentinel.PrivilegedBroker/Program.cs for mutation-order acceptance.");
        string source = File.ReadAllText(brokerProgram!);
        int verifyCall = source.IndexOf("QueryFirewallRuleForMutation(remoteIp)", StringComparison.Ordinal);
        int conflictRefusal = source.IndexOf("FirewallRuleConflict", StringComparison.Ordinal);
        int addBuild = source.IndexOf("BrokerFirewallPolicy.BuildAddArguments(remoteIp)", StringComparison.Ordinal);
        int deleteBuild = source.IndexOf("BrokerFirewallPolicy.BuildDeleteArguments(remoteIp)", StringComparison.Ordinal);
        Require(verifyCall >= 0 && conflictRefusal > verifyCall && addBuild > conflictRefusal && deleteBuild > conflictRefusal,
            "Broker firewall add/remove mutations are not source-ordered behind exact elevated preflight and conflict refusal.");
        Require(source.Contains("FirewallVerificationFailed", StringComparison.Ordinal),
            "Broker firewall query failure is not pinned to fail closed before mutation.");
        Require(source.Contains("No duplicate firewall rule was created", StringComparison.Ordinal),
            "Broker does not pin the exact-already-present add path to a safe no-op.");
        Require(source.Contains("already absent. No firewall change was needed", StringComparison.Ordinal),
            "Broker does not pin the already-absent remove path to a safe no-op.");
    }

    private static string Evidence(string remoteIp) =>
        $"FOUND=1\n" +
        "ENABLED=True\n" +
        "ACTION=Block\n" +
        "DIRECTION=Outbound\n" +
        "PROFILE=Any\n" +
        $"REMOTE={remoteIp}\n" +
        "LOCAL=Any\n" +
        "PROTOCOL=Any\n" +
        "LOCALPORT=Any\n" +
        "REMOTEPORT=Any\n" +
        "PROGRAM=Any\n" +
        "SERVICE=Any";

    private static string? FindBrokerProgram()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "src", "SentinelAI", "Sentinel.PrivilegedBroker", "Program.cs");
                if (File.Exists(candidate)) return candidate;
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
