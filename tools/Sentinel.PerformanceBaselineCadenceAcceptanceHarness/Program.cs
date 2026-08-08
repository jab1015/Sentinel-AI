using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Performance Baseline Cadence Acceptance ===");
int failures = 0;
void Check(string name, bool passed) { Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}"); if (!passed) failures++; }

var service = new PerformanceBaselineService();
var start = DateTime.Now;
SystemSnapshot Snapshot(DateTime timestamp, double cpu) => new()
{
    Timestamp = timestamp,
    CpuUsagePercent = cpu,
    MemoryUsagePercent = 40,
    DiskUsagePercent = 50,
    ProcessCount = 100
};

var first = service.Record(Snapshot(start, 10));
var rapid = service.Record(Snapshot(start.AddSeconds(2), 90));
var distinct = service.Record(Snapshot(start.AddMinutes(1), 20));

Check("First observation recorded", first.SampleCount == 1);
Check("Rapid security-cycle observation does not inflate history", rapid.SampleCount == 1);
Check("Rapid current value is still evaluated", rapid.CurrentCpuPercent == 90);
Check("One-minute observation advances history", distinct.SampleCount == 2);
Check("Baseline is not established from rapid checks", !distinct.IsEstablished);

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;
