using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Event-Driven Discovery Acceptance ===\n");
var service = new DiscoveryChangeDetectionService();
var failures = 0;
void Check(string name, bool pass) { Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}"); if (!pass) failures++; }

DiscoveryChangeDetectionService.ChangeDetectionResult Eval(
    bool fingerprint = false,
    bool security = false,
    bool critical = false,
    bool previousCritical = false,
    bool attention = false,
    bool previousAttention = false,
    bool suppressed = false,
    bool persistentChanged = false,
    bool powerChanged = false,
    bool idleChanged = false) =>
    service.Evaluate(new DiscoveryChangeDetectionService.ChangeDetectionInput(
        fingerprint, security, critical, previousCritical, attention, previousAttention,
        suppressed, persistentChanged, powerChanged, idleChanged));

Console.WriteLine("--- Scenario 1: unchanged evidence does not trigger event-driven recheck ---");
var s1 = Eval();
Check("No material change", !s1.MaterialChangeDetected);
Check("No immediate recheck", !s1.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 2: new critical evidence forces immediate recheck ---");
var s2 = Eval(critical: true, previousCritical: false);
Check("Critical change detected", s2.Kind == DiscoveryChangeDetectionService.ChangeKind.CriticalEvidenceAppeared);
Check("Immediate critical recheck", s2.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 3: security posture change forces immediate recheck ---");
var s3 = Eval(security: true);
Check("Security change detected", s3.Kind == DiscoveryChangeDetectionService.ChangeKind.SecurityPostureChanged);
Check("Immediate security recheck", s3.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 4: evidence fingerprint change invalidates unchanged-state assumption ---");
var s4 = Eval(fingerprint: true);
Check("Fingerprint change detected", s4.Kind == DiscoveryChangeDetectionService.ChangeKind.EvidenceFingerprintChanged);
Check("Fingerprint forces immediate recheck", s4.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 5: silently monitored persistent condition reopens on material change ---");
var s5 = Eval(suppressed: true, persistentChanged: true);
Check("Persistent change detected", s5.Kind == DiscoveryChangeDetectionService.ChangeKind.PersistentConditionChanged);
Check("Persistent condition reopens immediately", s5.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 6: attention transition recalculates scheduling ---");
var s6 = Eval(attention: true, previousAttention: false);
Check("Attention transition detected", s6.Kind == DiscoveryChangeDetectionService.ChangeKind.AttentionStateChanged);
Check("New attention forces immediate recheck", s6.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 7: cleared attention is material but does not force urgent recheck ---");
var s7 = Eval(attention: false, previousAttention: true);
Check("Attention cleared detected", s7.MaterialChangeDetected);
Check("Cleared attention not urgent", !s7.ForceImmediateRecheck);

Console.WriteLine("\n--- Scenario 8: operating-context changes recalculate cadence without urgent recheck ---");
var s8 = Eval(powerChanged: true);
Check("Operating context detected", s8.Kind == DiscoveryChangeDetectionService.ChangeKind.OperatingContextChanged);
Check("Operating context not urgent", !s8.ForceImmediateRecheck);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
