using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Live Adaptive Discovery Acceptance ===\n");
var scheduler = new LiveAdaptiveDiscoveryScheduler();
var failures = 0;
void Check(string label, bool pass)
{
    Console.WriteLine($"{label}: {(pass ? "PASS" : "FAIL")}");
    if (!pass) failures++;
}

SystemSnapshot Healthy() => new()
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    DefenderStatus = "Enabled",
    FirewallStatus = "Enabled",
    MemoryPressureLevel = "Normal",
    InvestigationRequiresAttention = false,
    GuidanceSeverity = "None",
    RiskLevel = "Low"
};

Console.WriteLine("--- Scenario 1: quiet live loop backs off from legacy five-second polling ---");
var quiet = scheduler.Evaluate(Healthy(), TimeSpan.FromSeconds(5));
Check("Quiet priority low", quiet.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Low);
Check("Quiet interval becomes 30 seconds", quiet.RecommendedInterval == TimeSpan.FromSeconds(30));
Check("Timer change requested", quiet.IntervalChanged);
Check("Monitoring remains enabled", quiet.MonitoringEnabled);

Console.WriteLine("\n--- Scenario 2: active attention keeps five-second cadence ---");
var attentionSnapshot = Healthy();
attentionSnapshot.InvestigationRequiresAttention = true;
var attention = scheduler.Evaluate(attentionSnapshot, TimeSpan.FromSeconds(30));
Check("Attention priority high", attention.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.High);
Check("Attention interval five seconds", attention.RecommendedInterval == TimeSpan.FromSeconds(5));
Check("Timer tightens", attention.IntervalChanged);

Console.WriteLine("\n--- Scenario 3: critical security posture tightens to two seconds ---");
var criticalSnapshot = Healthy();
criticalSnapshot.DefenderEnabled = false;
criticalSnapshot.DefenderStatus = "Disabled";
var critical = scheduler.Evaluate(criticalSnapshot, TimeSpan.FromSeconds(30));
Check("Critical priority", critical.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Critical);
Check("Critical interval two seconds", critical.RecommendedInterval == TimeSpan.FromSeconds(2));
Check("Deep verification retained", critical.AllowDeepVerification);

Console.WriteLine("\n--- Scenario 4: silently monitored persistent finding does not force high cadence ---");
var persistent = Healthy();
persistent.InvestigationRequiresAttention = true;
var silent = scheduler.Evaluate(persistent, TimeSpan.FromSeconds(5), persistentNotificationSuppressed: true);
Check("Silent condition priority low", silent.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Low);
Check("Silent condition backs off to 30 seconds", silent.RecommendedInterval == TimeSpan.FromSeconds(30));
Check("Monitoring remains enabled while notification suppressed", silent.MonitoringEnabled);

Console.WriteLine("\n--- Scenario 5: critical evidence overrides persistent suppression ---");
var suppressedCritical = Healthy();
suppressedCritical.InvestigationRequiresAttention = true;
suppressedCritical.GuidanceSeverity = "Critical";
var overrideDecision = scheduler.Evaluate(suppressedCritical, TimeSpan.FromSeconds(30), persistentNotificationSuppressed: true);
Check("Critical override preserved", overrideDecision.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Critical);
Check("Critical override interval two seconds", overrideDecision.RecommendedInterval == TimeSpan.FromSeconds(2));

Console.WriteLine("\n--- Scenario 6: unchanged interval does not request unnecessary timer reset ---");
var stable = scheduler.Evaluate(Healthy(), TimeSpan.FromSeconds(30));
Check("Stable interval unchanged", !stable.IntervalChanged);
Check("Stable interval remains 30 seconds", stable.RecommendedInterval == TimeSpan.FromSeconds(30));

Console.WriteLine("\n--- Scenario 7: battery mode lowers quiet-system impact without stopping monitoring ---");
var battery = scheduler.Evaluate(Healthy(), TimeSpan.FromSeconds(30), onBattery: true);
Check("Battery interval one minute", battery.RecommendedInterval == TimeSpan.FromMinutes(1));
Check("Battery timer change requested", battery.IntervalChanged);
Check("Battery monitoring remains enabled", battery.MonitoringEnabled);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
