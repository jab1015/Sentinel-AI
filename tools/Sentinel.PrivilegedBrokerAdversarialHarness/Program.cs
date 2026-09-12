using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static Dictionary<string, string> ExactFirewallEvidence(string remoteIp) => new(StringComparer.OrdinalIgnoreCase)
{
    ["FOUND"] = "1",
    ["ENABLED"] = "True",
    ["ACTION"] = "Block",
    ["DIRECTION"] = "Outbound",
    ["PROFILE"] = "Any",
    ["REMOTE"] = remoteIp,
    ["LOCAL"] = "Any",
    ["PROTOCOL"] = "Any",
    ["LOCALPORT"] = "Any",
    ["REMOTEPORT"] = "Any",
    ["PROGRAM"] = "Any",
    ["SERVICE"] = "Any"
};

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

const string testIp = "203.0.113.10";
FirewallRuleVerification exact = FirewallRuleVerificationPolicy.Evaluate(ExactFirewallEvidence(testIp), testIp);
Require(exact.QueryValid && exact.Exists && exact.IsExactBlock, "Exact enabled outbound Block rule was not accepted.");

Dictionary<string, string> disabled = ExactFirewallEvidence(testIp);
disabled["ENABLED"] = "False";
Require(!FirewallRuleVerificationPolicy.Evaluate(disabled, testIp).IsExactBlock, "Disabled rule was incorrectly accepted.");

Dictionary<string, string> allow = ExactFirewallEvidence(testIp);
allow["ACTION"] = "Allow";
Require(!FirewallRuleVerificationPolicy.Evaluate(allow, testIp).IsExactBlock, "Allow rule was incorrectly accepted.");

Dictionary<string, string> inbound = ExactFirewallEvidence(testIp);
inbound["DIRECTION"] = "Inbound";
Require(!FirewallRuleVerificationPolicy.Evaluate(inbound, testIp).IsExactBlock, "Inbound rule was incorrectly accepted.");

Dictionary<string, string> wrongRemote = ExactFirewallEvidence(testIp);
wrongRemote["REMOTE"] = "203.0.113.11";
Require(!FirewallRuleVerificationPolicy.Evaluate(wrongRemote, testIp).IsExactBlock, "Wrong remote address was incorrectly accepted.");

Dictionary<string, string> broadRemote = ExactFirewallEvidence(testIp);
broadRemote["REMOTE"] = testIp + ",203.0.113.11";
Require(!FirewallRuleVerificationPolicy.Evaluate(broadRemote, testIp).IsExactBlock, "Broader remote-address scope was incorrectly accepted.");

Dictionary<string, string> duplicate = ExactFirewallEvidence(testIp);
duplicate["FOUND"] = "2";
duplicate["CONFLICT"] = "True";
FirewallRuleVerification duplicateResult = FirewallRuleVerificationPolicy.Evaluate(duplicate, testIp);
Require(duplicateResult.QueryValid && duplicateResult.Exists && !duplicateResult.IsExactBlock,
    "Duplicate deterministic firewall rules did not fail closed.");

FirewallRuleVerification notFound = FirewallRuleVerificationPolicy.Evaluate(
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["FOUND"] = "0" }, testIp);
Require(notFound.QueryValid && !notFound.Exists, "Explicit firewall rule absence was not classified correctly.");

FirewallRuleVerification malformed = FirewallRuleVerificationPolicy.Evaluate(
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), testIp);
Require(!malformed.QueryValid, "Malformed firewall query output was incorrectly treated as verified absence.");

FirewallRuleVerification malformedCount = FirewallRuleVerificationPolicy.Evaluate(
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["FOUND"] = "not-a-number" }, testIp);
Require(!malformedCount.QueryValid, "Malformed firewall rule count was incorrectly treated as valid.");

Dictionary<string, string> incomplete = ExactFirewallEvidence(testIp);
incomplete.Remove("SERVICE");
FirewallRuleVerification incompleteResult = FirewallRuleVerificationPolicy.Evaluate(incomplete, testIp);
Require(incompleteResult.QueryValid && incompleteResult.Exists && !incompleteResult.IsExactBlock,
    "Incomplete firewall scope evidence was incorrectly accepted.");

Console.WriteLine("Same packaged broker/client identity: PASS");
Console.WriteLine("Different/missing package identity rejected: PASS");
Console.WriteLine("Firewall literal-IP validation: PASS");
Console.WriteLine("Firewall injection/non-literal targets rejected: PASS");
Console.WriteLine("Firewall mutation argument shape fixed/allowlisted: PASS");
Console.WriteLine("Firewall exact enabled outbound Block verification: PASS");
Console.WriteLine("Disabled/Allow/wrong-direction/wrong-scope rules rejected: PASS");
Console.WriteLine("Malformed firewall query output distinguished from verified rule absence: PASS");
Console.WriteLine("RESULT: PASS");
