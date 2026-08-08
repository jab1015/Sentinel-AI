using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Connection Intelligence Acceptance ===");
int failures = 0;
void Check(string name, bool passed) { Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}"); if (!passed) failures++; }
var engine = new ConnectionIntelligenceEngine();

var unavailable = engine.Analyze(new SystemSnapshot { NetworkConnectionMonitoringAvailable = false });
Check("Unavailable collection is degraded", unavailable.State == ConnectionIntelligenceEngine.ConnectionAssessmentState.Degraded);

var repeatingOnly = engine.Analyze(new SystemSnapshot
{
    NetworkConnectionMonitoringAvailable = true,
    ExternalConnectionCount = 1,
    AttributedExternalConnectionCount = 1,
    RecentUniqueExternalConnectionCount = 1,
    RepeatingExternalConnectionCount = 1
});
Check("Repeating traffic is observed without unsupported threat claim", repeatingOnly.State == ConnectionIntelligenceEngine.ConnectionAssessmentState.Observed);
Check("Repeating evidence contributes confidence", repeatingOnly.ConfidenceScore == 10);
Check("Repeating evidence alone is not corroboration", !repeatingOnly.HasCorroboratingEvidence);

var correlated = engine.Analyze(new SystemSnapshot
{
    NetworkConnectionMonitoringAvailable = true,
    ExternalConnectionCount = 1,
    AttributedExternalConnectionCount = 1,
    RecentUniqueExternalConnectionCount = 1,
    RepeatingExternalConnectionCount = 1,
    InboundExternalConnectionCount = 1,
    FlaggedConnectionCount = 1,
    PrimaryFlaggedConnectionProcessName = "sample.exe",
    FlaggedProcessCount = 1,
    PrimaryFlaggedProcessName = "sample"
});
Check("Process and connection evidence correlate", correlated.HasCorroboratingEvidence);
Check("Correlated repeated activity reaches investigation threshold", correlated.State == ConnectionIntelligenceEngine.ConnectionAssessmentState.Investigate);
Check("Correlation reason is evidence-specific", correlated.ReasonCode == "network-process-correlation");

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;

