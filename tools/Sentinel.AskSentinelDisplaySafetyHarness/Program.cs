using Sentinel.App.Models;
using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static AskSentinelResponseOrchestrator.AskSentinelResponse Response(string answer) =>
    new(
        answer,
        DateTimeOffset.UtcNow,
        3,
        RequiresAttention: false,
        IsInsufficientEvidence: false,
        UsedInvestigationHistory: false,
        UsedRecommendationGuard: false,
        PassedFinalSafetyValidation: false,
        GroundingSummary: "Harness evidence.");

Console.WriteLine("=== Sentinel AI Ask Sentinel Final Display Safety Acceptance ===");

var validator = new AskSentinelResponseSafetyValidator();
var snapshot = new SystemSnapshot();

var responder = new AskSentinelLocalResponder();
string broadOverview = responder.Answer("Tell me what's going on with my computer", snapshot);
Require(!broadOverview.Contains("not yet have enough verified information", StringComparison.OrdinalIgnoreCase),
    "Broad PC overview prompt fell back to insufficient evidence.");
Require(broadOverview.Contains("what’s going on with your computer", StringComparison.OrdinalIgnoreCase),
    "Broad PC overview prompt did not return the computer overview response.");
Require(broadOverview.Contains("I can go deeper", StringComparison.OrdinalIgnoreCase),
    "Broad PC overview response did not provide useful follow-up options.");
Require(broadOverview.Contains("external sources", StringComparison.OrdinalIgnoreCase) &&
        broadOverview.Contains("AI", StringComparison.OrdinalIgnoreCase),
    "Broad PC overview response did not expose the external research / AI follow-up path.");

var safeObserved = validator.ValidateForDisplay(
    Response("Sentinel observed a driver warning and recommends reviewing it."),
    snapshot,
    AskSentinelProvenanceLabel.Observed);
Require(safeObserved.IsSafe, "Ordinary deterministic observed text was blocked.");

string[] unsupportedActionClaims =
{
    "Sentinel blocked the attack.",
    "Sentinel quarantined the file.",
    "Sentinel repaired the issue.",
    "Sentinel removed the malicious file.",
    "Sentinel contained the connection.",
    "Microsoft Defender removed the malware.",
    "Microsoft Defender blocked the malware.",
    "The firewall rule was applied.",
    "Windows Firewall blocked the endpoint.",
    "The threat was quarantined.",
    "The threat was eliminated.",
    "The threat was removed.",
    "The malware was blocked.",
    "The file was removed."
};

foreach (string claim in unsupportedActionClaims)
{
    var advisory = validator.ValidateForDisplay(Response(claim), snapshot, AskSentinelProvenanceLabel.Advisory);
    Require(!advisory.IsSafe, $"Advisory action claim was accepted: {claim}");
    Require(advisory.Answer.Contains("not yet have enough verified information", StringComparison.OrdinalIgnoreCase),
        "Blocked advisory claim did not fall back to the insufficient-evidence answer.");

    var inferred = validator.ValidateForDisplay(Response(claim), snapshot, AskSentinelProvenanceLabel.Inferred);
    Require(!inferred.IsSafe, $"Inferred action claim was accepted: {claim}");
}

var verifiedAction = validator.ValidateForDisplay(
    Response("Sentinel repaired the issue."),
    snapshot,
    AskSentinelProvenanceLabel.ActionVerified);
Require(verifiedAction.IsSafe, "Deterministically verified action text was blocked.");

var empty = validator.ValidateForDisplay(Response("   "), snapshot, AskSentinelProvenanceLabel.Observed);
Require(!empty.IsSafe, "Empty final response was accepted.");

var noEvidence = new AskSentinelResponseOrchestrator.AskSentinelResponse(
    "Sentinel observed an issue.",
    DateTimeOffset.UtcNow,
    0,
    false,
    false,
    false,
    false,
    false,
    "Harness evidence.");
Require(!validator.ValidateForDisplay(noEvidence, snapshot, AskSentinelProvenanceLabel.Observed).IsSafe,
    "Response without evidence was accepted.");

var futureTimestamp = Response("Sentinel observed an issue.") with { EvidenceTimestamp = DateTimeOffset.UtcNow.AddHours(1) };
Require(!validator.ValidateForDisplay(futureTimestamp, snapshot, AskSentinelProvenanceLabel.Observed).IsSafe,
    "Response with invalid future evidence timestamp was accepted.");

Console.WriteLine("Broad PC overview response and follow-up choices: PASS");
Console.WriteLine("Observed deterministic response: PASS");
Console.WriteLine("Advisory/inferred false action claims rejected: PASS");
Console.WriteLine("Verified action provenance accepted: PASS");
Console.WriteLine("Empty/missing/invalid evidence rejected: PASS");
Console.WriteLine("RESULT: PASS");
