using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Friendly Value Activity Acceptance ===\n");

var service = new FriendlyValueActivityService();
int failures = 0;

void Check(string name, bool condition)
{
    Console.WriteLine($"{name}: {(condition ? "PASS" : "FAIL")}");
    if (!condition) failures++;
}

MaintenanceReportItem Item(string category, string summary, string outcome = "Verified", string action = "") =>
    new(DateTimeOffset.UtcNow, category, summary, outcome, false) { Action = action };

Console.WriteLine("--- Scenario 1: verified drive optimization becomes friendly value language ---");
var drive = service.CreateFor(Item("Optimization", "Drive optimization completed with retrim."));
Check("Drive summary created", drive is not null);
Check("Drive optimization described", drive?.Message.Contains("optimized your drive", StringComparison.OrdinalIgnoreCase) == true);
Check("Friendly housekeeping title used", drive?.Title.Contains("housekeeping", StringComparison.OrdinalIgnoreCase) == true);

Console.WriteLine("\n--- Scenario 2: verified temporary cleanup becomes friendly value language ---");
var cleanup = service.CreateFor(Item("Cleanup", "Removed stale temporary files and reclaimed disk space."));
Check("Cleanup summary created", cleanup is not null);
Check("Temporary cleanup described", cleanup?.Message.Contains("temporary files", StringComparison.OrdinalIgnoreCase) == true);

Console.WriteLine("\n--- Scenario 3: verified network repair uses reassuring repair language ---");
var network = service.CreateFor(Item("Network", "Network repair completed and DNS settings verified."));
Check("Network summary created", network is not null);
Check("Repair title used", network?.Title.Contains("took care of it", StringComparison.OrdinalIgnoreCase) == true);
Check("Network work described", network?.Message.Contains("network settings", StringComparison.OrdinalIgnoreCase) == true);

Console.WriteLine("\n--- Scenario 4: completed but unverified work is never celebrated ---");
var unverified = service.CreateFor(Item("Optimization", "Drive optimization completed.", "Completed"));
Check("Unverified work suppressed", unverified is null);

Console.WriteLine("\n--- Scenario 5: failed work is never presented as user value ---");
var failed = service.CreateFor(Item("Network", "Network repair attempted.", "Needs attention"));
Check("Failed work suppressed", failed is null);

Console.WriteLine("\n--- Scenario 6: verified system-file work maps to plain English ---");
var systemFiles = service.CreateFor(Item("Automatic Repair", "SFC system file repair completed and verified."));
Check("System file summary created", systemFiles is not null);
Check("Windows files described", systemFiles?.Message.Contains("Windows system files", StringComparison.OrdinalIgnoreCase) == true);
Check("No SFC jargon shown", systemFiles?.Message.Contains("SFC", StringComparison.OrdinalIgnoreCase) == false);

Console.WriteLine("\n--- Scenario 7: unknown verified technical work is not invented ---");
var unknown = service.CreateFor(Item("Other", "An unrelated technical operation completed."));
Check("Unknown action suppressed", unknown is null);

Console.WriteLine("\n--- Scenario 8: a generic driver mention is never presented as a repair ---");
var genericDriver = service.CreateFor(Item("Automatic Repair", "Driver repair completed and verified."));
Check("Generic driver claim suppressed", genericDriver is null);

Console.WriteLine("\n--- Scenario 9: driver detection or research is never presented as a repair ---");
var driverResearch = service.CreateFor(Item(
    "Automatic Repair",
    "Driver: Intel Management Engine Interface; investigation completed.",
    action: "Research driver"));
Check("Driver research suppressed", driverResearch is null);

Console.WriteLine("\n--- Scenario 10: an identified install with explicit post-repair verification may be surfaced ---");
var driver = service.CreateFor(Item(
    "Automatic Repair",
    "Device: Example Device; package: Example Driver 2.0; installed; post-repair verification passed.",
    action: "Install driver"));
Check("Evidence-backed driver summary created", driver is not null);
Check("Driver repair described", driver?.Message.Contains("driver repair", StringComparison.OrdinalIgnoreCase) == true);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
