using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Live Event-Driven Runtime Acceptance ===\n");

int failures = 0;
void Check(string name, bool condition)
{
    Console.WriteLine($"{name}: {(condition ? "PASS" : "FAIL")}");
    if (!condition) failures++;
}

SystemSnapshot Snapshot(
    string reason = "healthy",
    bool defender = true,
    bool firewall = true,
    bool critical = false,
    bool attention = false)
{
    return new SystemSnapshot
    {
        InvestigationReasonCode = reason,
        GuidanceTitle = reason,
        DefenderEnabled = defender,
        FirewallEnabled = firewall,
        InvestigationShouldEscalate = critical,
        GuidanceSeverity = critical ? "Critical" : "Healthy",
        RiskLevel = critical ? "Critical" : "Low",
        InvestigationRequiresAttention = attention,
        MemoryPressureLevel = "Normal",
        RemediationTarget = reason,
        PrimaryFlaggedConnectionRemoteEndpoint = "None",
        LatestEventSource = "None",
        FlaggedProcessCount = 0,
        FlaggedServiceCount = 0,
        FlaggedConnectionCount = 0,
        CriticalEventCount = critical ? 1 : 0,
        ErrorEventCount = 0
    };
}

var coordinator = new LiveEventDrivenDiscoveryCoordinator();

Console.WriteLine("--- Scenario 1: initial runtime observation does not request recursive refresh ---");
var initial = coordinator.Evaluate(Snapshot(), false);
Check("Initial observation quiet", !initial.MaterialChangeDetected);
Check("Initial observation not urgent", !initial.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 2: unchanged runtime observation stays on adaptive cadence ---");
var unchanged = coordinator.Evaluate(Snapshot(), false);
Check("Unchanged observation quiet", !unchanged.MaterialChangeDetected);
Check("No immediate confirmation refresh", !unchanged.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 3: material fingerprint change requests immediate confirmation refresh ---");
var fingerprint = coordinator.Evaluate(Snapshot("driver:intel-mei:code10"), false);
Check("Fingerprint change material", fingerprint.MaterialChangeDetected);
Check("Fingerprint change immediate", fingerprint.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 4: confirmation snapshot settles without refresh loop ---");
var confirmed = coordinator.Evaluate(Snapshot("driver:intel-mei:code10"), false);
Check("Confirmed snapshot unchanged", !confirmed.MaterialChangeDetected);
Check("Confirmation does not recurse", !confirmed.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 5: security posture transition interrupts ordinary cadence ---");
var security = coordinator.Evaluate(Snapshot("driver:intel-mei:code10", defender: false, critical: true, attention: true), false);
Check("Security transition material", security.MaterialChangeDetected);
Check("Security transition immediate", security.ForceImmediateRecheck);
Check("Security classification retained", security.Kind == DiscoveryChangeDetectionService.ChangeKind.SecurityPostureChanged);

Console.WriteLine("\n--- Scenario 6: silently monitored condition reopens when its evidence changes ---");
var silentCoordinator = new LiveEventDrivenDiscoveryCoordinator();
silentCoordinator.Evaluate(Snapshot("driver:intel-mei:code10"), true);
var reopened = silentCoordinator.Evaluate(Snapshot("driver:intel-mei:code31"), true);
Check("Silent condition change material", reopened.MaterialChangeDetected);
Check("Silent condition recheck immediate", reopened.ForceImmediateRecheck);
Check("Persistent classification retained", reopened.Kind == DiscoveryChangeDetectionService.ChangeKind.PersistentConditionChanged);

Console.WriteLine("\n--- Scenario 7: nonurgent operating-context change does not interrupt cadence ---");
var contextCoordinator = new LiveEventDrivenDiscoveryCoordinator();
contextCoordinator.Evaluate(Snapshot(), false, onBattery: false, applicationIsIdle: false);
var context = contextCoordinator.Evaluate(Snapshot(), false, onBattery: true, applicationIsIdle: false);
Check("Power transition material", context.MaterialChangeDetected);
Check("Power transition not urgent", !context.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 8: clearing attention recalculates without immediate confirmation refresh ---");
var attentionCoordinator = new LiveEventDrivenDiscoveryCoordinator();
attentionCoordinator.Evaluate(Snapshot("attention", attention: true), false);
var cleared = attentionCoordinator.Evaluate(Snapshot("attention", attention: false), false);
Check("Attention clearing material", cleared.MaterialChangeDetected);
Check("Attention clearing not urgent", !cleared.ForceImmediateRecheck);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
