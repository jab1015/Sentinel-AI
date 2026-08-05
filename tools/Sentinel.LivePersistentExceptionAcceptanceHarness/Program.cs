using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Live Persistent Exception Acceptance ===");
Console.WriteLine();

string root = Path.Combine(Path.GetTempPath(), "SentinelAI-LivePersistentExceptionAcceptance", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string store = Path.Combine(root, "investigations.json");
var memory = new PersistentInvestigationMemoryService(store);
var coordinator = new LivePersistentExceptionCoordinator(memory, new PersistentExceptionPresentationService());
int failures = 0;

void Check(string name, bool passed)
{
    Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}");
    if (!passed) failures++;
}

var evidence = new InvestigationInvalidationState(
    DeviceInstanceId: "Intel(R) Management Engine Interface",
    HardwareId: "PCI\\VEN_8086&DEV_8C3A",
    ErrorCode: "Code 10",
    DriverVersion: "11.0.5.1189",
    WindowsBuild: "10.0.22621",
    BiosVersion: "A14",
    Manufacturer: "Dell Inc.",
    Model: "XPS 8700",
    Severity: "Attention",
    VerifiedRepairSignature: "repair-v1");

var attempts = new List<RepairAttemptRecord>
{
    new("Windows Update", RepairAttemptOutcome.Unavailable, DateTimeOffset.UtcNow, "No matching update."),
    new("Microsoft Update Catalog", RepairAttemptOutcome.Unavailable, DateTimeOffset.UtcNow, "No exact package."),
    new("Computer manufacturer support", RepairAttemptOutcome.Unavailable, DateTimeOffset.UtcNow, "No model-specific repair."),
    new("Driver reinstall", RepairAttemptOutcome.Failed, DateTimeOffset.UtcNow, "Same failure returned."),
    new("Driver rollback", RepairAttemptOutcome.NotApplicable, DateTimeOffset.UtcNow, "No compatible rollback."),
    new("BIOS or firmware verification", RepairAttemptOutcome.Succeeded, DateTimeOffset.UtcNow, "BIOS current.")
};

string fingerprint = PersistentInvestigationMemoryService.CreateFingerprint("driver", evidence);
var record = new PersistentInvestigationRecord(
    Guid.NewGuid(), fingerprint, "Driver",
    "Intel(R) Management Engine Interface",
    "No verified repair currently exists.",
    95,
    "Authoritative Microsoft and manufacturer evidence",
    "Persistent noncritical",
    InvestigationLifecycleState.PersistentNoncritical,
    attempts,
    DateTimeOffset.UtcNow.AddDays(-1),
    DateTimeOffset.UtcNow,
    evidence,
    false,
    null,
    string.Empty);
await memory.UpsertAsync(record);

SystemSnapshot ActiveSnapshot() => new()
{
    InvestigationRequiresAttention = true,
    InvestigationReasonCode = "driver:intel(r) management engine interface",
    InvestigationSummary = "Windows reports a problem with Intel(R) Management Engine Interface (Code 10).",
    GuidanceTitle = "A driver needs attention",
    GuidanceWhatHappened = "Windows reports that Intel(R) Management Engine Interface (Code 10) may not be working correctly.",
    GuidanceEvidence = "Intel(R) Management Engine Interface is currently reporting Code 10."
};

Console.WriteLine("--- Scenario 1: live driver finding matches persistent memory ---");
var matched = await coordinator.EvaluateAsync(ActiveSnapshot());
Check("Matching memory found", matched.HasMatchingMemory && matched.Record is not null);
Check("Known condition offered", matched.ShowKnownCondition && !matched.SuppressNotification);
Check("Toggle available", matched.CanToggleNotifications);
Console.WriteLine();

Console.WriteLine("--- Scenario 2: live suppression hides notification but monitoring continues ---");
var suppress = await coordinator.SetSilentMonitoringAsync(record, true);
var suppressed = await coordinator.EvaluateAsync(ActiveSnapshot());
Check("Suppression accepted", suppress.Allowed);
Check("Live notification suppressed", suppressed.SuppressNotification);
Check("Background monitoring preserved", suppressed.Decision?.ContinueMonitoring == true);
Check("Resume notifications offered", suppressed.Decision?.ActionLabel == "Resume Notifications");
Console.WriteLine();

Console.WriteLine("--- Scenario 3: unrelated finding does not reuse driver exception ---");
var unrelatedSnapshot = ActiveSnapshot();
unrelatedSnapshot.InvestigationReasonCode = "secure-boot-disabled";
unrelatedSnapshot.GuidanceTitle = "Secure Boot is disabled";
unrelatedSnapshot.GuidanceWhatHappened = "Windows reports Secure Boot as disabled.";
unrelatedSnapshot.InvestigationSummary = "Secure Boot is currently disabled.";
var unrelated = await coordinator.EvaluateAsync(unrelatedSnapshot);
Check("Unrelated finding remains unmatched", !unrelated.HasMatchingMemory && !unrelated.SuppressNotification);
Console.WriteLine();

Console.WriteLine("--- Scenario 4: non-attention snapshot does not surface exception UI ---");
var healthy = ActiveSnapshot();
healthy.InvestigationRequiresAttention = false;
var healthyResult = await coordinator.EvaluateAsync(healthy);
Check("Healthy state remains quiet", !healthyResult.HasMatchingMemory && !healthyResult.SuppressNotification);
Console.WriteLine();

Console.WriteLine("--- Scenario 5: notifications resume without disabling monitoring ---");
var resume = await coordinator.SetSilentMonitoringAsync(suppress.Record!, false);
var resumed = await coordinator.EvaluateAsync(ActiveSnapshot());
Check("Resume accepted", resume.Allowed);
Check("Notification restored", resumed.ShowKnownCondition && !resumed.SuppressNotification);
Check("Monitoring still enabled", resumed.Decision?.ContinueMonitoring == true);
Console.WriteLine();

try { Directory.Delete(root, recursive: true); } catch { }

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;
