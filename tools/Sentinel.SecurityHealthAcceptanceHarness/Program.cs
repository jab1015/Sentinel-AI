using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI Security Health Classification Acceptance ===");

Require(SecurityHealthClassificationPolicy.ClassifyDefender(true, true, true, "Normal", 0) == "Enabled",
    "Healthy Defender state was not classified as Enabled.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(true, true, true, "Passive Mode", 0) == "Passive",
    "Passive Defender mode was not classified as Passive.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(false, true, true, "Normal", 0) == "Disabled or inactive",
    "Disabled Defender service was not rejected.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(true, false, true, "Normal", 0) == "Disabled or inactive",
    "Disabled antivirus engine was not rejected.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(true, true, false, "Normal", 0) == "Disabled or inactive",
    "Disabled real-time protection was not rejected.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(true, true, true, "Normal", 4) == "Enabled (signatures stale)",
    "Stale Defender signatures were not surfaced.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(null, true, true, "Normal", 0) == "Unavailable",
    "Missing Defender service evidence did not fail closed.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(true, null, true, "Normal", 0) == "Unavailable",
    "Malformed/missing antivirus evidence did not fail closed.");
Require(SecurityHealthClassificationPolicy.ClassifyDefender(true, true, null, "Normal", 0) == "Unavailable",
    "Malformed/missing real-time evidence did not fail closed.");

Require(SecurityHealthClassificationPolicy.ClassifyFirewall("Running", 3, 3, 3) == "Enabled",
    "Healthy three-profile firewall state was not classified as Enabled.");
Require(SecurityHealthClassificationPolicy.ClassifyFirewall("Stopped", 3, 3, 3) == "Disabled or inactive",
    "Stopped firewall service was not rejected.");
Require(SecurityHealthClassificationPolicy.ClassifyFirewall("Running", 3, 3, 0) == "Disabled",
    "All-disabled active firewall profiles were not classified as Disabled.");
Require(SecurityHealthClassificationPolicy.ClassifyFirewall("Running", 3, 3, 2) == "Partial (2 of 3 active profiles enabled)",
    "Partially enabled firewall profiles were not surfaced.");
Require(SecurityHealthClassificationPolicy.ClassifyFirewall("Running", 2, 2, 2) == "Unavailable",
    "Incomplete firewall profile evidence was incorrectly accepted.");
Require(SecurityHealthClassificationPolicy.ClassifyFirewall("Running", 3, 2, 2) == "Unavailable",
    "Observed firewall profile count mismatch did not fail closed.");
Require(SecurityHealthClassificationPolicy.ClassifyFirewall("Running", 3, 3, 4) == "Unavailable",
    "Impossible enabled-profile count did not fail closed.");
Require(SecurityHealthClassificationPolicy.ClassifyFirewall(string.Empty, 3, 3, 3) == "Disabled or inactive",
    "Missing firewall service state was incorrectly accepted.");

Console.WriteLine("Defender healthy/passive/disabled/stale classifications: PASS");
Console.WriteLine("Defender incomplete evidence fails closed: PASS");
Console.WriteLine("Firewall healthy/disabled/partial classifications: PASS");
Console.WriteLine("Firewall incomplete or impossible evidence fails closed: PASS");
Console.WriteLine("RESULT: PASS");
