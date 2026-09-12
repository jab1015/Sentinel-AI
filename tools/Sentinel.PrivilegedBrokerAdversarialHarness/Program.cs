static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI Privileged Broker Identity Acceptance ===");

const string packageA = "ModernMethods.SentinelAI_1.0.26.0_x64__sentinelpublisher";
const string packageB = "OtherPublisher.SentinelAI_1.0.26.0_x64__attacker";

Require(BrokerIdentityPolicy.SamePackage(packageA, packageA), "Exact package identity did not match itself.");
Require(BrokerIdentityPolicy.SamePackage(packageA.ToUpperInvariant(), packageA.ToLowerInvariant()), "Package identity comparison unexpectedly became case-sensitive.");
Require(!BrokerIdentityPolicy.SamePackage(packageA, packageB), "Different package identities were accepted.");
Require(!BrokerIdentityPolicy.SamePackage(packageA, null), "Missing client package identity was accepted.");
Require(!BrokerIdentityPolicy.SamePackage(null, packageA), "Missing broker package identity was accepted.");
Require(!BrokerIdentityPolicy.SamePackage(packageA, string.Empty), "Empty client package identity was accepted.");
Require(!BrokerIdentityPolicy.SamePackage("   ", packageA), "Whitespace broker package identity was accepted.");

Console.WriteLine("Same packaged broker/client identity: PASS");
Console.WriteLine("Different package identity rejected: PASS");
Console.WriteLine("Missing/unpackaged identity rejected: PASS");
Console.WriteLine("RESULT: PASS");
