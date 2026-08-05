using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Adaptive Discovery Diagnostics Acceptance ===\n");
int failures = 0;
void Check(string name, bool pass) { Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}"); if (!pass) failures++; }

var cadence = new AdaptiveDiscoveryCadenceService();
var scheduler = new LiveAdaptiveDiscoveryScheduler(cadence);
var diagnostics = new AdaptiveDiscoveryDiagnosticService();

SystemSnapshot Quiet() => new()
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    DefenderStatus = "Enabled",
    FirewallStatus = "Enabled",
    MemoryPressureLevel = "Normal"
};

Console.WriteLine("--- Scenario 1: initial quiet cadence produces one diagnostic event ---");
var quiet = scheduler.Evaluate(Quiet(), TimeSpan.FromSeconds(5));
var first = diagnostics.Evaluate(quiet, false);
Check("Initial event recorded", first.ShouldRecord);
Check("Initial event identifies adaptive discovery", first.Title.Contains("Adaptive Discovery", StringComparison.OrdinalIgnoreCase));
Check("Technical detail contains interval", first.TechnicalDetail.Contains("Interval: 30", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 2: unchanged cadence does not spam activity history ---");
var same = diagnostics.Evaluate(scheduler.Evaluate(Quiet(), TimeSpan.FromSeconds(30)), false);
Check("Duplicate event suppressed", !same.ShouldRecord);

Console.WriteLine("\n--- Scenario 3: attention transition records faster cadence ---");
var attentionSnapshot = Quiet();
attentionSnapshot.InvestigationRequiresAttention = true;
var attention = scheduler.Evaluate(attentionSnapshot, TimeSpan.FromSeconds(30));
var attentionEvent = diagnostics.Evaluate(attention, false);
Check("Attention transition recorded", attentionEvent.ShouldRecord);
Check("Five-second cadence explained", attentionEvent.Summary.Contains("5 seconds", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 4: critical transition explains urgent recheck ---");
var criticalSnapshot = Quiet();
criticalSnapshot.DefenderEnabled = false;
criticalSnapshot.DefenderStatus = "Disabled";
var critical = scheduler.Evaluate(criticalSnapshot, TimeSpan.FromSeconds(5));
var criticalEvent = diagnostics.Evaluate(critical, false);
Check("Critical transition recorded", criticalEvent.ShouldRecord);
Check("Critical explanation present", criticalEvent.Summary.Contains("critical", StringComparison.OrdinalIgnoreCase));
Check("Two-second interval present", criticalEvent.Summary.Contains("2 seconds", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 5: silent persistent condition explains continued monitoring ---");
var silentSnapshot = Quiet();
silentSnapshot.InvestigationRequiresAttention = true;
var silent = scheduler.Evaluate(silentSnapshot, TimeSpan.FromSeconds(2), persistentNotificationSuppressed: true);
var silentEvent = diagnostics.Evaluate(silent, true);
Check("Silent-monitoring transition recorded", silentEvent.ShouldRecord);
Check("Continued monitoring explained", silentEvent.Summary.Contains("continuing background monitoring", StringComparison.OrdinalIgnoreCase));
Check("Suppression state in technical detail", silentEvent.TechnicalDetail.Contains("suppressed: True", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("\n--- Scenario 6: diagnostics never imply monitoring is disabled ---");
Check("Scheduler reports monitoring enabled", silent.MonitoringEnabled);
Check("Technical detail confirms monitoring enabled", silentEvent.TechnicalDetail.Contains("Monitoring enabled: True", StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
