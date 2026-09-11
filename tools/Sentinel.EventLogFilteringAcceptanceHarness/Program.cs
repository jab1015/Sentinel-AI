using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI Event Log Filtering Acceptance ===");

Require(EventLogMonitor.IsKnownBenign("Service Control Manager", "The Microsoft Storage Spaces SMP service entered the stopped state."),
    "Known Storage Spaces benign event was not recognized.");
Console.WriteLine("Known Storage Spaces suppression: PASS");

Require(EventLogMonitor.IsKnownBenign("Microsoft-Windows-WindowsUpdateClient", "Installation failed with error 0x80073D02 because files were in use."),
    "Known Windows Update benign event was not recognized.");
Console.WriteLine("Known Windows Update suppression: PASS");

Require(!EventLogMonitor.IsKnownBenign("Service Control Manager", "The Security Center service terminated unexpectedly."),
    "An unrelated Service Control Manager error was incorrectly suppressed.");
Console.WriteLine("Unrelated service error preserved: PASS");

Require(!EventLogMonitor.IsKnownBenign("OtherProvider", "Microsoft Storage Spaces SMP experienced an unrelated error."),
    "Provider-independent text overlap incorrectly triggered suppression.");
Console.WriteLine("Provider mismatch preserved: PASS");

Require(!EventLogMonitor.IsKnownBenign("Microsoft-Windows-WindowsUpdateClient", "Installation failed with error 0x80070005 access denied."),
    "An unrelated Windows Update error was incorrectly suppressed.");
Console.WriteLine("Unrelated Windows Update error preserved: PASS");

Console.WriteLine("RESULT: PASS");
