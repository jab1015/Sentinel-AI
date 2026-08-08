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

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;
