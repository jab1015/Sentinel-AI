using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Remediation Outcome Acceptance ===");
int failures = 0;
void Check(string name, bool passed) { Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}"); if (!passed) failures++; }

SystemSnapshot snapshot = new()
{
    InvestigationRequiresAttention = true,
    InvestigationReasonCode = "test-condition",
    GuidanceConfidencePercent = 90,
    AutonomousProtectionRequiresUserApproval = true,
    AutonomousProtectionAction = "contain-process",
    AutonomousProtectionTarget = "already-exited.exe",
    PrimaryFlaggedProcessId = 101,
    PrimaryFlaggedProcessStartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
};
var approval = new RemediationApprovalCoordinator();
var request = approval.CreateRequest(snapshot)!;
var validation = approval.Validate(request, snapshot, true);
var replayValidation = approval.Validate(request, snapshot, true);
Check("Approval request cannot be replayed", !replayValidation.IsApproved);

var fabricated = request with { RequestId = Guid.NewGuid() };
var fabricatedValidation = approval.Validate(fabricated, snapshot, true);
Check("Fabricated approval request is rejected", !fabricatedValidation.IsApproved);

DateTimeOffset fakeNow = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
var expiryApproval = new RemediationApprovalCoordinator(() => fakeNow);
var expirySnapshot = new SystemSnapshot
{
    InvestigationRequiresAttention = true,
    InvestigationReasonCode = "expiry-test-condition",
    GuidanceConfidencePercent = 90,
    AutonomousProtectionRequiresUserApproval = true,
    AutonomousProtectionAction = "block-outbound-endpoint",
    AutonomousProtectionTarget = "203.0.113.40:443"
};
var expiringRequest = expiryApproval.CreateRequest(expirySnapshot)!;
fakeNow = fakeNow.AddMinutes(3);
var expiredValidation = expiryApproval.Validate(expiringRequest, expirySnapshot, true);
Check("Expired approval is rejected", !expiredValidation.IsApproved);

var changedTargetApproval = new RemediationApprovalCoordinator();
var changedTargetSnapshot = new SystemSnapshot
{
    InvestigationRequiresAttention = true,
    InvestigationReasonCode = "target-change-test",
    GuidanceConfidencePercent = 90,
    AutonomousProtectionRequiresUserApproval = true,
    AutonomousProtectionAction = "block-outbound-endpoint",
    AutonomousProtectionTarget = "203.0.113.50:443"
};
var changedTargetRequest = changedTargetApproval.CreateRequest(changedTargetSnapshot)!;
changedTargetSnapshot.AutonomousProtectionTarget = "203.0.113.51:443";
var changedTargetValidation = changedTargetApproval.Validate(changedTargetRequest, changedTargetSnapshot, true);
Check("Changed exact target invalidates approval", !changedTargetValidation.IsApproved);

var executor = new ApprovedRemediationExecutor();
bool delegateCalled = false;

var noChange = await executor.ExecuteAsync(snapshot, request, validation,
    executeAsync: () => { delegateCalled = true; return Task.CompletedTask; },
    verifyAsync: () => Task.FromResult(true),
    actionWasAttempted: () => false,
    noActionSummary: () => "The process had already exited. No change was needed.");

Check("Approved target rechecked", delegateCalled);
Check("No change is not marked attempted", !noChange.Attempted);
Check("No change is not marked verified repair", !noChange.Verified);
Check("No change keeps explicit outcome", noChange.Outcome == ApprovedRemediationExecutor.RemediationOutcome.NotAttempted);
Check("No-change explanation preserved", noChange.Summary.Contains("already exited", StringComparison.OrdinalIgnoreCase));

bool replayDelegateCalled = false;
var replayExecution = await executor.ExecuteAsync(snapshot, request, validation,
    executeAsync: () => { replayDelegateCalled = true; return Task.CompletedTask; },
    verifyAsync: () => Task.FromResult(true),
    actionWasAttempted: () => true);
Check("Approved execution cannot be replayed", !replayExecution.Attempted && !replayDelegateCalled);

SystemSnapshot secondSnapshot = new()
{
    InvestigationRequiresAttention = true,
    InvestigationReasonCode = "second-test-condition",
    GuidanceConfidencePercent = 90,
    AutonomousProtectionRequiresUserApproval = true,
    AutonomousProtectionAction = "contain-process",
    AutonomousProtectionTarget = "active-test.exe",
    PrimaryFlaggedProcessId = 202,
    PrimaryFlaggedProcessStartUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
};
var secondRequest = approval.CreateRequest(secondSnapshot)!;
var secondValidation = approval.Validate(secondRequest, secondSnapshot, true);
var changed = await executor.ExecuteAsync(secondSnapshot, secondRequest, secondValidation,
    executeAsync: () => Task.CompletedTask,
    verifyAsync: () => Task.FromResult(true),
    actionWasAttempted: () => true);

Check("Attempted and independently verified action succeeds", changed.Attempted && changed.Verified);
Check("Verified success outcome retained", changed.Outcome == ApprovedRemediationExecutor.RemediationOutcome.VerifiedSuccess);

SystemSnapshot identitySnapshot = new()
{
    InvestigationRequiresAttention = true,
    InvestigationReasonCode = "identity-test-condition",
    GuidanceConfidencePercent = 95,
    AutonomousProtectionRequiresUserApproval = true,
    AutonomousProtectionAction = "contain-process",
    AutonomousProtectionTarget = "identity-test.exe",
    PrimaryFlaggedProcessId = 303,
    PrimaryFlaggedProcessStartUtc = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)
};
var identityRequest = approval.CreateRequest(identitySnapshot)!;
identitySnapshot.PrimaryFlaggedProcessStartUtc =
    identitySnapshot.PrimaryFlaggedProcessStartUtc.Value.AddSeconds(1);
var identityValidation = approval.Validate(identityRequest, identitySnapshot, true);
Check("Replacement process invalidates approval", !identityValidation.IsApproved);

Console.WriteLine();
Console.WriteLine("--- Defender file-quarantine approval identity ---");
{
    var defenderApproval = new RemediationApprovalCoordinator();
    var defenderSnapshot = new SystemSnapshot
    {
        InvestigationRequiresAttention = true,
        InvestigationReasonCode = "defender-active-file-threat",
        GuidanceConfidencePercent = 100,
        AutonomousProtectionRequiresUserApproval = true,
        AutonomousProtectionAction = "quarantine-file",
        AutonomousProtectionTarget = @"C:\Temp\defender-threat.exe",
        DefenderThreatEvidenceAvailable = true,
        DefenderActiveThreatCount = 1,
        DefenderFileQuarantineCandidateAvailable = true,
        DefenderPrimaryThreatId = 123456789,
        DefenderPrimaryThreatFilePath = @"C:\Temp\defender-threat.exe",
        DefenderPrimaryThreatName = "Trojan:Win32/SentinelAcceptance"
    };

    var defenderRequest = defenderApproval.CreateRequest(defenderSnapshot)!;
    Check("Defender quarantine approval captures exact Threat ID",
        defenderRequest.TargetDefenderThreatId == 123456789);

    defenderSnapshot.DefenderPrimaryThreatId = 987654321;
    var changedThreatValidation = defenderApproval.Validate(defenderRequest, defenderSnapshot, true);
    Check("Different Defender Threat ID at same path invalidates approval",
        !changedThreatValidation.IsApproved);

    var candidateApproval = new RemediationApprovalCoordinator();
    defenderSnapshot.DefenderPrimaryThreatId = 123456789;
    defenderSnapshot.DefenderFileQuarantineCandidateAvailable = true;
    var candidateRequest = candidateApproval.CreateRequest(defenderSnapshot)!;
    defenderSnapshot.DefenderFileQuarantineCandidateAvailable = false;
    var missingCandidateValidation = candidateApproval.Validate(candidateRequest, defenderSnapshot, true);
    Check("Disappearing Defender file candidate invalidates approval",
        !missingCandidateValidation.IsApproved);

    var stableApproval = new RemediationApprovalCoordinator();
    defenderSnapshot.DefenderFileQuarantineCandidateAvailable = true;
    var stableRequest = stableApproval.CreateRequest(defenderSnapshot)!;
    var stableValidation = stableApproval.Validate(stableRequest, defenderSnapshot, true);
    Check("Unchanged Defender Threat ID and exact target retain approval",
        stableValidation.IsApproved);
}

Console.WriteLine();
Console.WriteLine("--- Approved remediation routing ---");
Check("Service action routes to service coordinator",
    ApprovedRemediationRoutingPolicy.Resolve("restart-service") == ApprovedRemediationRoute.ServiceRestart);
Check("Network action routes to firewall coordinator",
    ApprovedRemediationRoutingPolicy.Resolve("block-outbound-endpoint") == ApprovedRemediationRoute.FirewallContainment);
Check("Process action routes to process coordinator",
    ApprovedRemediationRoutingPolicy.Resolve("contain-process") == ApprovedRemediationRoute.ProcessContainment);
Check("File quarantine routes to quarantine coordinator",
    ApprovedRemediationRoutingPolicy.Resolve("quarantine-file") == ApprovedRemediationRoute.Quarantine);
Check("File restore routes to quarantine coordinator",
    ApprovedRemediationRoutingPolicy.Resolve("restore-quarantined-file") == ApprovedRemediationRoute.Quarantine);
Check("File delete routes to quarantine coordinator",
    ApprovedRemediationRoutingPolicy.Resolve("delete-quarantined-file") == ApprovedRemediationRoute.Quarantine);
Check("Unknown approved action fails closed",
    ApprovedRemediationRoutingPolicy.Resolve("do-something-unknown") == ApprovedRemediationRoute.Unsupported);
Check("Empty approved action fails closed",
    ApprovedRemediationRoutingPolicy.Resolve(" ") == ApprovedRemediationRoute.Unsupported);

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;
