using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Crash Evidence Acceptance ===");
int failures = 0;
void Check(string name, bool passed) { Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}"); if (!passed) failures++; }
var now = DateTime.Now;

var clean = WindowsCrashEvidenceMonitor.Analyze(Array.Empty<WindowsCrashEvidenceMonitor.CrashEventEvidence>(), null, true);
Check("No evidence does not invent a crash", !clean.CrashDetected && !clean.RootCauseVerified);

var unexpectedRestart = WindowsCrashEvidenceMonitor.Analyze(new[]
{
    new WindowsCrashEvidenceMonitor.CrashEventEvidence(41, now, "Microsoft-Windows-Kernel-Power", "The system rebooted without cleanly shutting down first.")
}, null, true);
Check("Kernel-Power confirms abnormal restart", unexpectedRestart.CrashDetected);
Check("Kernel-Power alone is not called a blue screen", !unexpectedRestart.BugCheckDetected);
Check("Kernel-Power does not invent root cause", !unexpectedRestart.RootCauseVerified);

var unrelated1001 = WindowsCrashEvidenceMonitor.Analyze(new[]
{
    new WindowsCrashEvidenceMonitor.CrashEventEvidence(1001, now, "Unrelated-Windows-Provider", "A non-crash event used the same numeric event ID.")
}, null, true);
Check("Unrelated Event ID 1001 is not treated as BugCheck", !unrelated1001.BugCheckDetected && !unrelated1001.CrashDetected);

var bugCheck = WindowsCrashEvidenceMonitor.Analyze(new[]
{
    new WindowsCrashEvidenceMonitor.CrashEventEvidence(1001, now, "Microsoft-Windows-WER-SystemErrorReporting", "The computer has rebooted from a bugcheck. The bugcheck was: 0x00000133.")
}, new WindowsCrashEvidenceMonitor.MinidumpEvidence(now, 4096), true);
Check("BugCheck confirms blue-screen evidence", bugCheck.BugCheckDetected);
Check("Stop code retained", bugCheck.BugCheckCode == "0X00000133");
Check("Minidump is reported without reading contents", bugCheck.Summary.Contains("has not read or uploaded", StringComparison.OrdinalIgnoreCase));
Check("BugCheck does not name unsupported cause", !bugCheck.RootCauseVerified && bugCheck.Summary.Contains("does not identify", StringComparison.OrdinalIgnoreCase));

var unavailable = WindowsCrashEvidenceMonitor.Analyze(Array.Empty<WindowsCrashEvidenceMonitor.CrashEventEvidence>(), null, false);
Check("Unavailable evidence is explicit", !unavailable.CollectionAvailable && !unavailable.CrashDetected);

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;

