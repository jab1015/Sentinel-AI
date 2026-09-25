using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Discovery Acceptance ===");
Console.WriteLine();

var investigationEngine = new InvestigationEngine();
var remediationEngine = new RemediationRecommendationEngine();
var protectionStatusService = new ProtectionStatusSummaryService();
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
    ProtectionHealthState = "Protected",
    ProtectionHealthFullyProtected = true,
    ProtectionHealthReasonCode = "protection-healthy",
    ProtectionHealthSummary = "Microsoft Defender, Windows Firewall, and Sentinel monitoring evidence are healthy.",
    NetworkConnectionMonitoringAvailable = true,
    NetworkConnectionMonitoringStatus = "Available",
    ConnectionIntelligenceState = "Normal",
    ConnectionIntelligenceReasonCode = "network-normal",
    SpywareCorrelationState = "Normal",
    SpywareCorrelationReasonCode = "spyware-normal",
    AuthenticationAnomalyState = "Normal",
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
    snapshot.DefenderStatus = "Disabled or inactive";
    snapshot.ProtectionHealthFullyProtected = false;
    snapshot.ProtectionHealthState = "Degraded";
    snapshot.ProtectionHealthReasonCode = "security-protection-disabled";
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

Console.WriteLine("--- Scenario 3b: high-confidence corroborated network finding requires approval ---");
{
    var snapshot = BaseHealthy();
    snapshot.FlaggedConnectionCount = 1;
    snapshot.PrimaryFlaggedConnectionProcessName = "Unknown process";
    snapshot.PrimaryFlaggedConnectionRemoteEndpoint = "20.44.17.102:8883";
    snapshot.PrimaryFlaggedConnectionReason = "Rare external destination with corroborating security evidence.";
    snapshot.ConnectionIntelligenceConfidenceScore = 88;
    snapshot.ConnectionIntelligenceHasCorroboratingEvidence = true;
    snapshot.ConnectionIntelligenceState = "HighConcern";
    snapshot.ConnectionIntelligenceSummary = "The endpoint is corroborated by independent local security evidence.";
    var result = investigationEngine.Investigate(snapshot);
    snapshot.InvestigationRequiresAttention = result.RequiresAttention;
    snapshot.InvestigationReasonCode = result.ReasonCode;
    snapshot.GuidanceConfidencePercent = 88;
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Corroborated network reason retained", result.ReasonCode == "corroborated-network-finding");
    Check("Corroborated network approval required", remediation.Disposition == RemediationRecommendationEngine.RemediationDisposition.ApprovalRequired);
    Check("Exact network block action prepared", remediation.Action == "block-outbound-endpoint");
    Check("Exact endpoint preserved", remediation.Target == "20.44.17.102:8883");
    snapshot.AutonomousProtectionRequiresUserApproval = true;
    snapshot.AutonomousProtectionAction = remediation.Action;
    snapshot.AutonomousProtectionTarget = remediation.Target;
    var status = protectionStatusService.Create(snapshot);
    Check("Protection status says network containment is ready", status.Headline.Contains("ready for review", StringComparison.OrdinalIgnoreCase));
    Check("Protection status preserves approval requirement", status.ActionState.Contains("approval", StringComparison.OrdinalIgnoreCase));
}
Console.WriteLine();

Console.WriteLine("--- Scenario 3c: flagged network evidence without corroboration is not promoted ---");
{
    var snapshot = BaseHealthy();
    snapshot.FlaggedConnectionCount = 1;
    snapshot.PrimaryFlaggedConnectionProcessName = "Unknown process";
    snapshot.PrimaryFlaggedConnectionRemoteEndpoint = "203.0.113.20:443";
    snapshot.PrimaryFlaggedConnectionReason = "Rare external destination.";
    snapshot.ConnectionIntelligenceConfidenceScore = 70;
    snapshot.ConnectionIntelligenceHasCorroboratingEvidence = false;
    snapshot.ConnectionIntelligenceState = "Review";
    var result = investigationEngine.Investigate(snapshot);
    snapshot.InvestigationRequiresAttention = result.RequiresAttention;
    snapshot.InvestigationReasonCode = result.ReasonCode;
    snapshot.GuidanceConfidencePercent = 70;
    var remediation = remediationEngine.Evaluate(snapshot);
    Check("Uncorroborated network remains under review", result.ReasonCode == "network-evidence-under-review");
    Check("No containment action exposed", remediation.Action != "block-outbound-endpoint");
    Check("No approval-gated network mutation", remediation.Disposition != RemediationRecommendationEngine.RemediationDisposition.ApprovalRequired);
    var status = protectionStatusService.Create(snapshot);
    Check("Protection status explains missing corroboration", status.CurrentResponse.Contains("corroborating evidence has not been established", StringComparison.OrdinalIgnoreCase));
    Check("Protection status exposes the 80 percent network threshold", status.ActionCriteria.Contains("80%", StringComparison.OrdinalIgnoreCase));
    Check("Protection status says no containment is authorized", status.ActionState.Contains("No network containment is authorized", StringComparison.OrdinalIgnoreCase));
}
Console.WriteLine();

Console.WriteLine("--- Scenario 3d: actionable remediation outranks unrelated lower-confidence flags ---");
{
    var snapshot = BaseHealthy();
    snapshot.FlaggedConnectionCount = 1;
    snapshot.PrimaryFlaggedConnectionRemoteEndpoint = "203.0.113.77:443";
    snapshot.PrimaryFlaggedConnectionProcessName = "unknown";
    snapshot.ConnectionIntelligenceConfidenceScore = 20;
    snapshot.ConnectionIntelligenceHasCorroboratingEvidence = false;
    snapshot.FlaggedProcessCount = 1;
    snapshot.PrimaryFlaggedProcessName = "suspicious.exe";
    snapshot.AutonomousProtectionRequiresUserApproval = true;
    snapshot.AutonomousProtectionAction = "contain-process";
    snapshot.AutonomousProtectionTarget = "suspicious.exe";
    var status = protectionStatusService.Create(snapshot);
    Check("Actionable process decision outranks passive network flag", status.Headline.Contains("Process containment is ready", StringComparison.OrdinalIgnoreCase));
    Check("Action state still requires approval", status.ActionState.Contains("approval", StringComparison.OrdinalIgnoreCase));
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
