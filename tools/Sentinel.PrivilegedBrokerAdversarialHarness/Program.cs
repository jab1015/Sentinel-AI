static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI Privileged Broker Adversarial Acceptance ===");

const string packageA = "ModernMethods.SentinelAI_1.0.26.0_x64__sentinelpublisher";
const string packageB = "OtherPublisher.SentinelAI_1.0.26.0_x64__attacker";

Require(BrokerIdentityPolicy.SamePackage(packageA, packageA), "Exact package identity did not match itself.");
Require(BrokerIdentityPolicy.SamePackage(packageA.ToUpperInvariant(), packageA.ToLowerInvariant()), "Package identity comparison unexpectedly became case-sensitive.");
Require(!BrokerIdentityPolicy.SamePackage(packageA, packageB), "Different package identities were accepted.");
Require(!BrokerIdentityPolicy.SamePackage(packageA, null), "Missing client package identity was accepted.");
Require(!BrokerIdentityPolicy.SamePackage(null, packageA), "Missing broker package identity was accepted.");
Require(!BrokerIdentityPolicy.SamePackage(packageA, string.Empty), "Empty client package identity was accepted.");
Require(!BrokerIdentityPolicy.SamePackage("   ", packageA), "Whitespace broker package identity was accepted.");

Require(BrokerFirewallPolicy.TryNormalizeRemoteIp("203.0.113.10", out string ipv4) && ipv4 == "203.0.113.10",
    "Literal IPv4 target was rejected or changed unexpectedly.");
Require(BrokerFirewallPolicy.TryNormalizeRemoteIp("2001:db8::10", out string ipv6) && ipv6.Contains(':', StringComparison.Ordinal),
    "Literal IPv6 target was rejected.");

string[] maliciousTargets =
{
    "203.0.113.10 & net user attacker /add",
    "203.0.113.10;Remove-Item C:\\*",
    "203.0.113.10 profile=any action=allow",
    "203.0.113.10\" dir=in action=allow",
    "any",
    "*",
    "example.com",
    "",
    "   "
};
foreach (string target in maliciousTargets)
    Require(!BrokerFirewallPolicy.TryNormalizeRemoteIp(target, out _), $"Non-literal/injected firewall target was accepted: '{target}'");

string[] addArgs = BrokerFirewallPolicy.BuildAddArguments("203.0.113.10");
Require(addArgs.SequenceEqual(new[]
{
    "advfirewall", "firewall", "add", "rule",
    $"name={BrokerFirewallPolicy.BuildRuleName("203.0.113.10")}",
    "dir=out", "action=block", "remoteip=203.0.113.10", "enable=yes", "profile=any"
}), "Firewall block arguments differ from the fixed allowlisted shape.");

string[] deleteArgs = BrokerFirewallPolicy.BuildDeleteArguments("203.0.113.10");
Require(deleteArgs.SequenceEqual(new[]
{
    "advfirewall", "firewall", "delete", "rule",
    $"name={BrokerFirewallPolicy.BuildRuleName("203.0.113.10")}"
}), "Firewall removal arguments differ from the fixed allowlisted shape.");

Console.WriteLine("Same packaged broker/client identity: PASS");
Console.WriteLine("Different/missing package identity rejected: PASS");
Console.WriteLine("Firewall literal-IP validation: PASS");
Console.WriteLine("Firewall injection/non-literal targets rejected: PASS");
Console.WriteLine("Firewall mutation argument shape fixed/allowlisted: PASS");
Console.WriteLine("RESULT: PASS");
