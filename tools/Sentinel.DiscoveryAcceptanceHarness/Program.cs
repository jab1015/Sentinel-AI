using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Discovery Acceptance ===");
Console.WriteLine();

var investigationEngine = new InvestigationEngine();
var remediationEngine = new RemediationRecommendationEngine();
int failures = 0;

void Check(string name, bool passed)
{
    Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}");
    if (!passed) failures++;
}

SystemSnapshot BaseHealthy() => new()
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    DefenderStatus = "Enabled",
    FirewallStatus = "Enabled",
    LatestEventSource = "None",
    LatestEventMessage = "No actionable Windows events were detected.",
    PrimaryFlaggedServiceName = "None",
    PrimaryFlaggedProcessName = "None",
    PrimaryFlaggedConnectionRemoteEndpoint = "None",
    InvestigationReasonCode = "healthy"
};

Console.WriteLine("--- Scenario 1: healthy evidence remains quiet ---");
{
    var snapshot = BaseHealthy();
    var result = investigationEngine.Investigate(snapshot);
    Check("Healthy state", result.State == InvestigationEngine.InvestigationState.NoIssue);
    Check("No attention required", !result.RequiresAttention);
}
Console.WriteLine();

Console.WriteLine("--- Scenario 2: Defender disabled is proactive and actionable ---");
{
    var snapshot = BaseHealthy();
    snapshot.DefenderEnabled = false;
    var result = investigationEngine.Investigate(snapshot);
    snapshot.InvestigationRequiresAttention = result.RequiresAttention;
    snapshot.InvestigationReasonCode = result.ReasonCode;
    snapshot.GuidanceConfidencePercent = 96;
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Discovery requires attention", result.RequiresAttention);
    Check("Safe automatic classification", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.SafeAutomatic);
    Check("Security refresh action", remediation.Action == "refresh-security-state");
}
Console.WriteLine();

Console.WriteLine("--- Scenario 3: correlated network behavior requires approval ---");
{
    var snapshot = BaseHealthy();
    snapshot.FlaggedProcessCount = 1;
    snapshot.PrimaryFlaggedProcessName = "sample.exe";
    snapshot.PrimaryFlaggedProcessReason = "Process requires review.";
    snapshot.FlaggedConnectionCount = 1;
    snapshot.PrimaryFlaggedConnectionProcessName = "sample.exe";
    snapshot.PrimaryFlaggedConnectionRemoteEndpoint = "203.0.113.10:443";
    snapshot.PrimaryFlaggedConnectionReason = "Connection requires review.";
    var result = investigationEngine.Investigate(snapshot);
    snapshot.InvestigationRequiresAttention = result.RequiresAttention;
    snapshot.InvestigationReasonCode = result.ReasonCode;
    snapshot.GuidanceConfidencePercent = 90;
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Correlated network finding", result.ReasonCode == "correlated-process-network-finding");
    Check("Approval required", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.ApprovalRequired);
    Check("Block action prepared", remediation.Action == "block-outbound-endpoint");
}
Console.WriteLine();

Console.WriteLine("--- Scenario 4: uncorroborated process evidence stays observation-only ---");
{
    var snapshot = BaseHealthy();
    snapshot.FlaggedProcessCount = 1;
    snapshot.PrimaryFlaggedProcessName = "unknown.exe";
    snapshot.PrimaryFlaggedProcessReason = "Unusual process evidence.";
    var result = investigationEngine.Investigate(snapshot);
    snapshot.InvestigationRequiresAttention = result.RequiresAttention;
    snapshot.InvestigationReasonCode = result.ReasonCode;
    snapshot.GuidanceConfidencePercent = 72;
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Investigation only", result.State == InvestigationEngine.InvestigationState.Investigating);
    Check("No system-changing action", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.None || remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.ObserveOnly);
}
Console.WriteLine();

Console.WriteLine("--- Scenario 5: driver finding is guided and approval-gated ---");
{
    var snapshot = BaseHealthy();
    snapshot.InvestigationRequiresAttention = true;
    snapshot.InvestigationReasonCode = "driver:intel management engine interface";
    snapshot.GuidanceTitle = "A driver needs attention";
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Guided driver action", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.GuidedUserAction);
    Check("Driver review action", remediation.Action == "review-driver-repair");
    Check("Approval retained", remediation.RequiresUserApproval);
}
Console.WriteLine();

Console.WriteLine("--- Scenario 6: Windows Update is guided, not silently installed ---");
{
    var snapshot = BaseHealthy();
    snapshot.InvestigationRequiresAttention = true;
    snapshot.InvestigationReasonCode = "windows-updates-pending";
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Guided update action", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.GuidedUserAction);
    Check("Open Windows Update", remediation.Action == "open-windows-update");
    Check("User approval required", remediation.RequiresUserApproval);
}
Console.WriteLine();

Console.WriteLine("--- Scenario 7: Secure Boot remains guided firmware action ---");
{
    var snapshot = BaseHealthy();
    snapshot.InvestigationRequiresAttention = true;
    snapshot.InvestigationReasonCode = "secure-boot-disabled";
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Guided Secure Boot action", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.GuidedUserAction);
    Check("No automatic firmware change", remediation.RequiresUserApproval);
}
Console.WriteLine();

Console.WriteLine("--- Scenario 8: critical disk pressure is guided ---");
{
    var snapshot = BaseHealthy();
    snapshot.InvestigationRequiresAttention = true;
    snapshot.InvestigationReasonCode = "disk-space-critical";
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Guided storage action", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.GuidedUserAction);
    Check("Open Storage action", remediation.Action == "open-storage");
}
Console.WriteLine();

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures} check(s) failed)");
Environment.ExitCode = failures == 0 ? 0 : 1;
