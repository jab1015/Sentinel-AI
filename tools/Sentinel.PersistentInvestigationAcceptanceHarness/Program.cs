using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Persistent Investigation Acceptance ===");
Console.WriteLine();

string root = Path.Combine(Path.GetTempPath(), "SentinelAI-PersistentInvestigationAcceptance", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string store = Path.Combine(root, "investigations.json");
var service = new PersistentInvestigationMemoryService(store);
var presentation = new PersistentExceptionPresentationService();

int failures = 0;

void Check(string name, bool passed)
{
    Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}");
    if (!passed) failures++;
}

InvestigationInvalidationState BaseState(string repairSignature = "repair-v1", string errorCode = "Code 10") => new(
    DeviceInstanceId: "Intel(R) Management Engine Interface",
    HardwareId: "PCI\\VEN_8086&DEV_8C3A",
    ErrorCode: errorCode,
    DriverVersion: "11.0.5.1189",
    WindowsBuild: "10.0.22621",
    BiosVersion: "A14",
    Manufacturer: "Dell Inc.",
    Model: "XPS 8700",
    Severity: "Attention",
    VerifiedRepairSignature: repairSignature);

PersistentInvestigationRecord CreateRecord(
    InvestigationLifecycleState state,
    IReadOnlyList<RepairAttemptRecord> attempts,
    InvestigationInvalidationState evidence,
    bool suppressed = false)
{
    string fingerprint = PersistentInvestigationMemoryService.CreateFingerprint("driver", evidence);
    return new PersistentInvestigationRecord(
        Guid.NewGuid(), fingerprint, "Driver",
        "Intel(R) Management Engine Interface",
        "No verified repair currently exists.",
        95,
        "Authoritative Microsoft and manufacturer evidence",
        state == InvestigationLifecycleState.Critical ? "Critical" : "Persistent noncritical",
        state,
        attempts,
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow,
        evidence,
        suppressed,
        suppressed ? DateTimeOffset.UtcNow : null,
        suppressed ? "User selected silent monitoring after verified exhaustive remediation." : string.Empty);
}

var terminalAttempts = new List<RepairAttemptRecord>
{
    new("Windows Update", RepairAttemptOutcome.Unavailable, DateTimeOffset.UtcNow, "No matching update."),
    new("Microsoft Update Catalog", RepairAttemptOutcome.Unavailable, DateTimeOffset.UtcNow, "No exact compatible package."),
    new("Computer manufacturer support", RepairAttemptOutcome.Unavailable, DateTimeOffset.UtcNow, "No model-specific repair."),
    new("Driver reinstall", RepairAttemptOutcome.Failed, DateTimeOffset.UtcNow, "Windows reinstalled the same failing driver."),
    new("Driver rollback", RepairAttemptOutcome.NotApplicable, DateTimeOffset.UtcNow, "No earlier compatible driver."),
    new("BIOS or firmware verification", RepairAttemptOutcome.Succeeded, DateTimeOffset.UtcNow, "BIOS is current; condition remains.")
};

Console.WriteLine("--- Scenario 1: incomplete investigation cannot be silenced ---");
var incomplete = CreateRecord(
    InvestigationLifecycleState.InvestigationIncomplete,
    new[] { new RepairAttemptRecord("Windows Update", RepairAttemptOutcome.Unavailable, DateTimeOffset.UtcNow, "No package.") },
    BaseState());
await service.UpsertAsync(incomplete);
var incompleteDecision = await service.SetSilentMonitoringAsync(incomplete.Fingerprint, true, "User requested silence.");
Check("Suppression rejected", !incompleteDecision.Allowed);
Console.WriteLine();

Console.WriteLine("--- Scenario 2: critical condition cannot be silenced ---");
var criticalState = BaseState("critical-repair-v1", "Code 43");
var critical = CreateRecord(InvestigationLifecycleState.Critical, terminalAttempts, criticalState);
await service.UpsertAsync(critical);
var criticalDecision = await service.SetSilentMonitoringAsync(critical.Fingerprint, true, "User requested silence.");
Check("Critical suppression rejected", !criticalDecision.Allowed);
Console.WriteLine();

Console.WriteLine("--- Scenario 3: exhausted noncritical condition can enter silent monitoring ---");
var persistentState = BaseState();
var persistent = CreateRecord(InvestigationLifecycleState.PersistentNoncritical, terminalAttempts, persistentState);
await service.UpsertAsync(persistent);
var accepted = await service.SetSilentMonitoringAsync(persistent.Fingerprint, true, "No verified repair remains; monitor silently.");
Check("Suppression accepted", accepted.Allowed);
Check("Suppression persisted", accepted.Record?.NotificationsSuppressed == true);
Console.WriteLine();

Console.WriteLine("--- Scenario 4: unchanged evidence reuses verified memory ---");
var reusable = await service.FindReusableAsync(persistent.Fingerprint, persistentState);
Check("Stored conclusion reused", reusable is not null && reusable.NotificationsSuppressed);
Console.WriteLine();

Console.WriteLine("--- Scenario 5: material evidence change invalidates prior conclusion ---");
var changedState = persistentState with { DriverVersion = "12.0.0.1000" };
var invalidated = await service.FindReusableAsync(persistent.Fingerprint, changedState);
Check("Changed driver invalidates memory", invalidated is null);
Check("Material change detected", PersistentInvestigationMemoryService.HasMaterialChange(persistentState, changedState));
Console.WriteLine();

Console.WriteLine("--- Scenario 6: notifications can be resumed without disabling monitoring ---");
var resumed = await service.SetSilentMonitoringAsync(persistent.Fingerprint, false, string.Empty);
Check("Resume accepted", resumed.Allowed);
Check("Notifications restored", resumed.Record?.NotificationsSuppressed == false);
Console.WriteLine();

Console.WriteLine("--- Scenario 7: incomplete finding remains active in presentation policy ---");
var incompletePresentation = presentation.Evaluate(incomplete);
Check("Incomplete finding remains active", incompletePresentation.ShowAsActiveFinding && !incompletePresentation.SuppressNotification);
Check("Monitoring remains enabled", incompletePresentation.ContinueMonitoring);
Console.WriteLine();

Console.WriteLine("--- Scenario 8: completed condition offers silent monitoring before suppression ---");
var knownPresentation = presentation.Evaluate(persistent);
Check("Known condition shown", knownPresentation.ShowKnownCondition && !knownPresentation.ShowAsActiveFinding);
Check("Silent monitoring action offered", knownPresentation.ActionLabel == "Monitor Silently");
Console.WriteLine();

Console.WriteLine("--- Scenario 9: suppressed condition is hidden but still monitored ---");
var suppressedPresentation = presentation.Evaluate(accepted.Record);
Check("Notification hidden", suppressedPresentation.SuppressNotification && !suppressedPresentation.ShowAsActiveFinding);
Check("Background monitoring continues", suppressedPresentation.ContinueMonitoring);
Check("Resume action offered", suppressedPresentation.ActionLabel == "Resume Notifications");
Console.WriteLine();

Console.WriteLine("--- Scenario 10: critical presentation can never be hidden ---");
var criticalPresentation = presentation.Evaluate(critical);
Check("Critical condition remains active", criticalPresentation.ShowAsActiveFinding);
Check("Critical notification not suppressed", !criticalPresentation.SuppressNotification);
Console.WriteLine();

try { Directory.Delete(root, recursive: true); } catch { }

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;
