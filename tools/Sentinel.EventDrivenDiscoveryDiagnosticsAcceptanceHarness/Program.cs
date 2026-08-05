using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Event-Driven Discovery Diagnostics Acceptance ===\n");

int failures = 0;
void Check(string name, bool condition)
{
    Console.WriteLine($"{name}: {(condition ? "PASS" : "FAIL")}");
    if (!condition) failures++;
}

var diagnostics = new EventDrivenDiscoveryDiagnosticService();

LiveEventDrivenDiscoveryCoordinator.LiveEventDrivenDecision Decision(
    bool material,
    DiscoveryChangeDetectionService.ChangeKind kind,
    bool immediate,
    string reason) => new(material, kind, immediate, reason);

Console.WriteLine("--- Scenario 1: unchanged evidence produces no diagnostic noise ---");
var unchanged = diagnostics.Evaluate(Decision(false, DiscoveryChangeDetectionService.ChangeKind.None, false, "No material Discovery evidence change was detected."));
Check("Unchanged event suppressed", !unchanged.ShouldRecord);

Console.WriteLine("\n--- Scenario 2: fingerprint change records immediate recheck explanation ---");
var fingerprint = diagnostics.Evaluate(Decision(true, DiscoveryChangeDetectionService.ChangeKind.EvidenceFingerprintChanged, true, "The evidence fingerprint changed."));
Check("Fingerprint event recorded", fingerprint.ShouldRecord);
Check("Immediate recheck explained", fingerprint.Summary.Contains("immediate confirmation recheck", StringComparison.OrdinalIgnoreCase));
Check("Monitoring enabled retained", fingerprint.TechnicalDetail.Contains("Monitoring enabled: true", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 3: duplicate identical event is suppressed ---");
var duplicate = diagnostics.Evaluate(Decision(true, DiscoveryChangeDetectionService.ChangeKind.EvidenceFingerprintChanged, true, "The evidence fingerprint changed."));
Check("Duplicate event suppressed", !duplicate.ShouldRecord);

Console.WriteLine("\n--- Scenario 4: security posture change receives specific title ---");
var security = diagnostics.Evaluate(Decision(true, DiscoveryChangeDetectionService.ChangeKind.SecurityPostureChanged, true, "Security posture changed."));
Check("Security event recorded", security.ShouldRecord);
Check("Security title retained", security.Title.Contains("Security posture changed", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 5: silent persistent condition change explains reopening path ---");
var persistent = diagnostics.Evaluate(Decision(true, DiscoveryChangeDetectionService.ChangeKind.PersistentConditionChanged, true, "A silently monitored persistent condition materially changed and must be reopened."));
Check("Persistent event recorded", persistent.ShouldRecord);
Check("Persistent title retained", persistent.Title.Contains("Known condition changed", StringComparison.OrdinalIgnoreCase));
Check("Immediate recheck retained", persistent.TechnicalDetail.Contains("Immediate recheck: True", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 6: operating-context change remains nonurgent ---");
var context = diagnostics.Evaluate(Decision(true, DiscoveryChangeDetectionService.ChangeKind.OperatingContextChanged, false, "System operating conditions changed."));
Check("Context event recorded", context.ShouldRecord);
Check("Nonurgent recalculation explained", context.Summary.Contains("without forcing an urgent refresh", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 7: diagnostics never imply monitoring is disabled ---");
Check("Monitoring stays enabled", context.TechnicalDetail.Contains("Monitoring enabled: true", StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
