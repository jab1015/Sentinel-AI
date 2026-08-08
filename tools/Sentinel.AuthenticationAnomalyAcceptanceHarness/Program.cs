using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Authentication Anomaly Acceptance ===");
int failures = 0;
void Check(string name, bool passed) { Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}"); if (!passed) failures++; }
var now = DateTime.Now;

var healthy = AuthenticationAnomalyMonitor.Analyze(Array.Empty<AuthenticationAnomalyMonitor.FailedAuthenticationEvidence>(), true);
Check("Empty Security log is healthy", healthy.State == "Healthy" && !healthy.SuspiciousPattern);

var isolated = AuthenticationAnomalyMonitor.Analyze(new[]
{
    new AuthenticationAnomalyMonitor.FailedAuthenticationEvidence(now, "203.0.113.10", "UserA"),
    new AuthenticationAnomalyMonitor.FailedAuthenticationEvidence(now, "203.0.113.11", "UserB")
}, true);
Check("Isolated failures remain observing", isolated.State == "Observing" && !isolated.SuspiciousPattern);

var repeated = Enumerable.Range(0, 6)
    .Select(index => new AuthenticationAnomalyMonitor.FailedAuthenticationEvidence(now.AddSeconds(-index), "203.0.113.50", "Administrator"))
    .ToArray();
var suspicious = AuthenticationAnomalyMonitor.Analyze(repeated, true);
Check("Repeated remote source is suspicious", suspicious.SuspiciousPattern && suspicious.RepeatedSourceFailureCount == 6);
Check("Suspicious pattern has actionable confidence", suspicious.ConfidenceScore >= 65);
Check("Evidence summary identifies source", suspicious.Summary.Contains("203.0.113.50", StringComparison.Ordinal));

var localOnly = Enumerable.Range(0, 6)
    .Select(index => new AuthenticationAnomalyMonitor.FailedAuthenticationEvidence(now.AddSeconds(-index), "127.0.0.1", "LocalUser"))
    .ToArray();
var local = AuthenticationAnomalyMonitor.Analyze(localOnly, true);
Check("Local failures do not create remote-source correlation", !local.SuspiciousPattern);

var unavailable = AuthenticationAnomalyMonitor.Analyze(Array.Empty<AuthenticationAnomalyMonitor.FailedAuthenticationEvidence>(), false);
Check("Unavailable collection is explicit", unavailable.State == "Unavailable" && !unavailable.CollectionAvailable);

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;
