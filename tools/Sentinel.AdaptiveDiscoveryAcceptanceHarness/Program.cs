using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Adaptive Discovery Acceptance ===\n");
int failures = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}");
    if (!pass) failures++;
}

var service = new AdaptiveDiscoveryCadenceService();

Console.WriteLine("--- Scenario 1: critical security posture gets immediate cadence ---");
var critical = new SystemSnapshot
{
    DefenderEnabled = false,
    FirewallEnabled = true
};
var criticalDecision = service.Evaluate(critical);
Check("Critical priority", criticalDecision.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Critical);
Check("Critical interval <= 2 seconds", criticalDecision.NextCheckInterval <= TimeSpan.FromSeconds(2));
Check("Deep verification allowed", criticalDecision.AllowDeepVerification);

Console.WriteLine("\n--- Scenario 2: attention finding gets high cadence ---");
var high = new SystemSnapshot
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    InvestigationRequiresAttention = true,
    GuidanceSeverity = "Attention"
};
var highDecision = service.Evaluate(high);
Check("High priority", highDecision.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.High);
Check("High interval <= 5 seconds", highDecision.NextCheckInterval <= TimeSpan.FromSeconds(5));

Console.WriteLine("\n--- Scenario 3: moderate evidence uses medium cadence ---");
var medium = new SystemSnapshot
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    MemoryPressureLevel = "High"
};
var mediumDecision = service.Evaluate(medium);
Check("Medium priority", mediumDecision.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Medium);
Check("Medium interval 15 seconds on AC", mediumDecision.NextCheckInterval == TimeSpan.FromSeconds(15));

Console.WriteLine("\n--- Scenario 4: battery reduces noncritical cadence ---");
var batteryDecision = service.Evaluate(medium, onBattery: true);
Check("Battery remains medium", batteryDecision.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Medium);
Check("Battery interval is reduced to 30 seconds", batteryDecision.NextCheckInterval == TimeSpan.FromSeconds(30));

Console.WriteLine("\n--- Scenario 5: quiet idle computer permits deeper background verification ---");
var quiet = new SystemSnapshot
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    MemoryPressureLevel = "Normal"
};
var idleDecision = service.Evaluate(quiet, applicationIsIdle: true, onBattery: false);
Check("Idle remains low priority", idleDecision.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Low);
Check("Idle deep verification allowed", idleDecision.AllowDeepVerification);
Check("Idle interval 15 seconds", idleDecision.NextCheckInterval == TimeSpan.FromSeconds(15));

Console.WriteLine("\n--- Scenario 6: quiet battery system uses lowest-impact cadence ---");
var quietBattery = service.Evaluate(quiet, applicationIsIdle: false, onBattery: true);
Check("Quiet battery low priority", quietBattery.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Low);
Check("Quiet battery interval one minute", quietBattery.NextCheckInterval == TimeSpan.FromMinutes(1));
Check("Monitoring remains scheduled", quietBattery.NextCheckInterval > TimeSpan.Zero);

Console.WriteLine("\n--- Scenario 7: escalation overrides ordinary attention ---");
var escalated = new SystemSnapshot
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    InvestigationRequiresAttention = true,
    InvestigationShouldEscalate = true
};
var escalationDecision = service.Evaluate(escalated);
Check("Escalation is critical", escalationDecision.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Critical);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
