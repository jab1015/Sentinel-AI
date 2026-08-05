using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Live Event-Driven Discovery Acceptance ===\n");
int failures = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}");
    if (!pass) failures++;
}

SystemSnapshot Healthy(string reason = "healthy") => new()
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    InvestigationRequiresAttention = false,
    InvestigationReasonCode = reason,
    GuidanceTitle = "No issue",
    GuidanceSeverity = "Healthy",
    RiskLevel = "Low",
    MemoryPressureLevel = "Normal",
    LatestEventSource = "None",
    RemediationTarget = "None",
    PrimaryFlaggedConnectionRemoteEndpoint = "None"
};

var coordinator = new LiveEventDrivenDiscoveryCoordinator();

Console.WriteLine("--- Scenario 1: initial state is captured without false event ---");
var first = coordinator.Evaluate(Healthy(), false);
Check("Initial state not material", !first.MaterialChangeDetected);
Check("Initial state not urgent", !first.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 2: unchanged live snapshot remains quiet ---");
var unchanged = coordinator.Evaluate(Healthy(), false);
Check("Unchanged snapshot quiet", !unchanged.MaterialChangeDetected);

Console.WriteLine("\n--- Scenario 3: live fingerprint change forces immediate recheck ---");
var changedFingerprintSnapshot = Healthy("driver-code-10");
changedFingerprintSnapshot.GuidanceTitle = "Driver condition detected";
changedFingerprintSnapshot.InvestigationRequiresAttention = true;
var fingerprint = coordinator.Evaluate(changedFingerprintSnapshot, false);
Check("Fingerprint change detected", fingerprint.MaterialChangeDetected);
Check("Fingerprint change urgent", fingerprint.ForceImmediateRecheck);
Check("Fingerprint kind retained", fingerprint.Kind == DiscoveryChangeDetectionService.ChangeKind.EvidenceFingerprintChanged);

Console.WriteLine("\n--- Scenario 4: Defender transition is immediate security event ---");
coordinator.Reset();
coordinator.Evaluate(Healthy(), false);
var defenderOff = Healthy();
defenderOff.DefenderEnabled = false;
var security = coordinator.Evaluate(defenderOff, false);
Check("Security posture detected", security.MaterialChangeDetected);
Check("Security posture urgent", security.ForceImmediateRecheck);
Check("Security kind retained", security.Kind == DiscoveryChangeDetectionService.ChangeKind.SecurityPostureChanged);

Console.WriteLine("\n--- Scenario 5: new critical escalation forces immediate recheck ---");
coordinator.Reset();
coordinator.Evaluate(Healthy(), false);
var critical = Healthy();
critical.InvestigationShouldEscalate = true;
var escalation = coordinator.Evaluate(critical, false);
Check("Critical evidence detected", escalation.MaterialChangeDetected);
Check("Critical evidence urgent", escalation.ForceImmediateRecheck);
Check("Critical kind retained", escalation.Kind == DiscoveryChangeDetectionService.ChangeKind.CriticalEvidenceAppeared);

Console.WriteLine("\n--- Scenario 6: silently monitored condition reopens on material change ---");
coordinator.Reset();
var persistent = Healthy("persistent-driver");
persistent.GuidanceTitle = "Known driver condition";
coordinator.Evaluate(persistent, true);
var persistentChanged = coordinator.Evaluate(persistent, true, persistentConditionMateriallyChanged: true);
Check("Persistent material change detected", persistentChanged.MaterialChangeDetected);
Check("Persistent material change urgent", persistentChanged.ForceImmediateRecheck);
Check("Persistent change kind retained", persistentChanged.Kind == DiscoveryChangeDetectionService.ChangeKind.PersistentConditionChanged);

Console.WriteLine("\n--- Scenario 7: attention appearance is urgent but clearing is not ---");
coordinator.Reset();
coordinator.Evaluate(Healthy(), false);
var needsAttention = Healthy();
needsAttention.InvestigationRequiresAttention = true;
var attentionOn = coordinator.Evaluate(needsAttention, false);
Check("Attention appearance detected", attentionOn.MaterialChangeDetected);
Check("Attention appearance urgent", attentionOn.ForceImmediateRecheck);
var attentionOff = coordinator.Evaluate(Healthy(), false);
Check("Attention clearing detected", attentionOff.MaterialChangeDetected);
Check("Attention clearing not urgent", !attentionOff.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 8: power and idle changes recalculate without urgent recheck ---");
coordinator.Reset();
coordinator.Evaluate(Healthy(), false, onBattery: false, applicationIsIdle: false);
var context = coordinator.Evaluate(Healthy(), false, onBattery: true, applicationIsIdle: true);
Check("Operating context detected", context.MaterialChangeDetected);
Check("Operating context not urgent", !context.ForceImmediateRecheck);
Check("Operating context kind retained", context.Kind == DiscoveryChangeDetectionService.ChangeKind.OperatingContextChanged);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
