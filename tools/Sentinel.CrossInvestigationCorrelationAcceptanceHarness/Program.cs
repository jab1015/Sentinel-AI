using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Cross-Investigation Correlation Acceptance ===");
Console.WriteLine();

var engine = new CrossInvestigationCorrelationEngine();
int failures = 0;

void Check(string name, bool passed)
{
    Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}");
    if (!passed) failures++;
}

SystemSnapshot Healthy() => new()
{
    DefenderEnabled = true,
    FirewallEnabled = true,
    DefenderStatus = "Enabled",
    FirewallStatus = "Enabled",
    LatestEventSource = "None",
    LatestEventMessage = "No actionable Windows events were detected.",
    GuidanceSeverity = "Healthy"
};

Console.WriteLine("--- Scenario 1: healthy evidence produces no investigations ---");
var healthy = engine.Analyze(Healthy());
Check("No observations", healthy.ObservationCount == 0);
Check("No correlated investigations", healthy.InvestigationCount == 0);
Check("No attention required", !healthy.RequiresAttention);
Console.WriteLine();

Console.WriteLine("--- Scenario 2: process and network signals correlate without claiming verified root cause ---");
var processNetwork = Healthy();
processNetwork.FlaggedProcessCount = 1;
processNetwork.PrimaryFlaggedProcessName = "example.exe";
processNetwork.PrimaryFlaggedProcessReason = "Unexpected execution behavior.";
processNetwork.FlaggedConnectionCount = 1;
processNetwork.PrimaryFlaggedConnectionReason = "Unexpected external connection.";
processNetwork.ConnectionIntelligenceConfidenceScore = 90;
processNetwork.ConnectionIntelligenceHasCorroboratingEvidence = true;
var processNetworkResult = engine.Analyze(processNetwork);
var processGroup = processNetworkResult.Groups.FirstOrDefault(g => g.CorrelationId == "correlation:process-behavior");
Check("Signals grouped", processGroup is not null && processGroup.ObservationIds.Count >= 2);
Check("Correlation confidence elevated", processGroup is not null && processGroup.ConfidencePercent >= 85);
Check("Root cause not falsely verified", processGroup is not null && !processGroup.IsVerifiedRootCause);
Console.WriteLine();

Console.WriteLine("--- Scenario 3: matching service and event evidence becomes one verified investigation ---");
var serviceEvent = Healthy();
serviceEvent.FlaggedServiceCount = 1;
serviceEvent.PrimaryFlaggedServiceName = "Example Service";
serviceEvent.PrimaryFlaggedServiceReason = "Example Service terminated unexpectedly.";
serviceEvent.ErrorEventCount = 1;
serviceEvent.LatestEventSource = "Service Control Manager";
serviceEvent.LatestEventMessage = "The Example Service terminated unexpectedly.";
var serviceEventResult = engine.Analyze(serviceEvent);
var serviceGroup = serviceEventResult.Groups.FirstOrDefault(g => g.CorrelationId == "correlation:service-event");
Check("Service and event grouped", serviceGroup is not null && serviceGroup.ObservationIds.Count == 2);
Check("Verified relationship retained", serviceGroup is not null && serviceGroup.IsVerifiedRootCause);
Check("Duplicate warning count reduced", serviceEventResult.InvestigationCount < serviceEventResult.ObservationCount);
Console.WriteLine();

Console.WriteLine("--- Scenario 4: unrelated service and event evidence stays independent ---");
var unrelated = Healthy();
unrelated.FlaggedServiceCount = 1;
unrelated.PrimaryFlaggedServiceName = "Print Spooler";
unrelated.PrimaryFlaggedServiceReason = "Print Spooler requires review.";
unrelated.ErrorEventCount = 1;
unrelated.LatestEventSource = "Disk";
unrelated.LatestEventMessage = "Storage subsystem reported an unrelated error.";
var unrelatedResult = engine.Analyze(unrelated);
Check("No unsupported service-event correlation", unrelatedResult.Groups.All(g => g.CorrelationId != "correlation:service-event"));
Check("Independent investigations retained", unrelatedResult.InvestigationCount == unrelatedResult.ObservationCount);
Console.WriteLine();

Console.WriteLine("--- Scenario 5: security controls remain critical and unsuppressed by correlation ---");
var security = Healthy();
security.DefenderEnabled = false;
security.FirewallEnabled = false;
var securityResult = engine.Analyze(security);
var securityGroup = securityResult.Groups.FirstOrDefault(g => g.CorrelationId == "correlation:security-controls");
Check("Security controls grouped", securityGroup is not null && securityGroup.ObservationIds.Count == 2);
Check("Critical severity preserved", securityGroup is not null && securityGroup.Severity == "Critical");
Check("Verified critical root cause", securityGroup is not null && securityGroup.IsVerifiedRootCause && securityGroup.RequiresAttention);
Console.WriteLine();

Console.WriteLine("--- Scenario 6: driver and matching event evidence are presented as one investigation ---");
var driver = Healthy();
driver.InvestigationRequiresAttention = true;
driver.InvestigationReasonCode = "driver:intel(r) management engine interface";
driver.GuidanceTitle = "Intel(R) Management Engine Interface driver needs attention";
driver.GuidanceSeverity = "Attention";
driver.GuidanceConfidencePercent = 100;
driver.InvestigationSummary = "Windows reports Intel(R) Management Engine Interface (Code 10).";
driver.GuidanceWhatHappened = driver.InvestigationSummary;
driver.ErrorEventCount = 1;
driver.LatestEventSource = "Kernel-PnP";
driver.LatestEventMessage = "Intel(R) Management Engine Interface reported Code 10.";
var driverResult = engine.Analyze(driver);
var driverGroup = driverResult.Groups.FirstOrDefault(g => g.CorrelationId == "correlation:driver");
Check("Driver investigation present", driverGroup is not null);
Check("Driver event evidence grouped", driverGroup is not null && driverGroup.ObservationIds.Count == 2);
Check("Driver correlation confidence high", driverGroup is not null && driverGroup.ConfidencePercent >= 98);
Console.WriteLine();

Console.WriteLine("--- Scenario 7: primary investigation favors critical evidence ---");
var mixed = Healthy();
mixed.DefenderEnabled = false;
mixed.FlaggedProcessCount = 1;
mixed.PrimaryFlaggedProcessName = "example.exe";
mixed.PrimaryFlaggedProcessReason = "Unexpected behavior.";
var mixedResult = engine.Analyze(mixed);
Check("Critical investigation selected primary", mixedResult.PrimaryCorrelationId == "correlation:security-controls");
Check("Primary severity critical", mixedResult.Severity == "Critical");
Console.WriteLine();

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;
