using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Live Event-Driven Runtime Acceptance ===\n");

int failures = 0;
void Check(string name, bool condition)
{
    Console.WriteLine($"{name}: {(condition ? "PASS" : "FAIL")}");
    if (!condition) failures++;
}

var coordinator = new LiveEventDrivenDiscoveryCoordinator();

LiveEventDrivenDiscoveryCoordinator.LiveDiscoveryState State(
    string fingerprint = "healthy",
    string defender = "On",
    string firewall = "On",
    bool critical = false,
    bool attention = false,
    bool suppressed = false,
    bool onBattery = false,
    bool idle = false) => new(
        fingerprint,
        defender,
        firewall,
        critical,
        attention,
        suppressed,
        onBattery,
        idle);

Console.WriteLine("--- Scenario 1: initial runtime observation does not request recursive refresh ---");
var initial = coordinator.Evaluate(State());
Check("Initial observation quiet", !initial.MaterialChangeDetected);
Check("Initial observation not urgent", !initial.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 2: unchanged runtime observation stays on adaptive cadence ---");
var unchanged = coordinator.Evaluate(State());
Check("Unchanged observation quiet", !unchanged.MaterialChangeDetected);
Check("No immediate confirmation refresh", !unchanged.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 3: material fingerprint change requests immediate confirmation refresh ---");
var fingerprint = coordinator.Evaluate(State(fingerprint: "driver:intel-mei:code10"));
Check("Fingerprint change material", fingerprint.MaterialChangeDetected);
Check("Fingerprint change immediate", fingerprint.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 4: confirmation snapshot settles without refresh loop ---");
var confirmed = coordinator.Evaluate(State(fingerprint: "driver:intel-mei:code10"));
Check("Confirmed snapshot unchanged", !confirmed.MaterialChangeDetected);
Check("Confirmation does not recurse", !confirmed.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 5: security posture transition interrupts ordinary cadence ---");
var security = coordinator.Evaluate(State(fingerprint: "driver:intel-mei:code10", defender: "Off", critical: true, attention: true));
Check("Security transition material", security.MaterialChangeDetected);
Check("Security transition immediate", security.ForceImmediateRecheck);
Check("Security classification retained", security.Kind == DiscoveryChangeDetectionService.ChangeKind.SecurityPostureChanged);

Console.WriteLine("\n--- Scenario 6: silently monitored condition reopens when its evidence changes ---");
var silentCoordinator = new LiveEventDrivenDiscoveryCoordinator();
silentCoordinator.Evaluate(State(fingerprint: "driver:intel-mei:code10", suppressed: true));
var reopened = silentCoordinator.Evaluate(State(fingerprint: "driver:intel-mei:code31", suppressed: true));
Check("Silent condition change material", reopened.MaterialChangeDetected);
Check("Silent condition recheck immediate", reopened.ForceImmediateRecheck);
Check("Persistent classification retained", reopened.Kind == DiscoveryChangeDetectionService.ChangeKind.PersistentConditionChanged);

Console.WriteLine("\n--- Scenario 7: nonurgent operating-context change does not interrupt cadence ---");
var contextCoordinator = new LiveEventDrivenDiscoveryCoordinator();
contextCoordinator.Evaluate(State());
var context = contextCoordinator.Evaluate(State(onBattery: true));
Check("Power transition material", context.MaterialChangeDetected);
Check("Power transition not urgent", !context.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 8: clearing attention recalculates without immediate confirmation refresh ---");
var attentionCoordinator = new LiveEventDrivenDiscoveryCoordinator();
attentionCoordinator.Evaluate(State(attention: true));
var cleared = attentionCoordinator.Evaluate(State(attention: false));
Check("Attention clearing material", cleared.MaterialChangeDetected);
Check("Attention clearing not urgent", !cleared.ForceImmediateRecheck);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
