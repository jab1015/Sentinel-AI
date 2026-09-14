using Sentinel.App.Models;
using Sentinel.App.Services;
using System.Text.Json;

Console.WriteLine("=== Sentinel AI Performance Baseline Cadence Acceptance ===");
int failures = 0;
void Check(string name, bool passed) { Console.WriteLine($"{name}: {(passed ? "PASS" : "FAIL")}"); if (!passed) failures++; }

string root = Path.Combine(Path.GetTempPath(), "SentinelPerformanceBaselineAcceptance", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    SystemSnapshot Snapshot(DateTime timestamp, double cpu) => new()
    {
        Timestamp = timestamp,
        CpuUsagePercent = cpu,
        MemoryUsagePercent = 40,
        DiskUsagePercent = 50,
        ProcessCount = 100,
        DownloadMbps = 5,
        UploadMbps = 2
    };

    string cadencePath = Path.Combine(root, "cadence.json");
    var service = new PerformanceBaselineService(cadencePath);
    DateTime start = DateTime.UtcNow.AddMinutes(-3);

    var first = service.Record(Snapshot(start, 10));
    var rapid = service.Record(Snapshot(start.AddSeconds(2), 90));
    var distinct = service.Record(Snapshot(start.AddMinutes(1), 20));

    Check("First observation recorded", first.SampleCount == 1);
    Check("Rapid security-cycle observation does not inflate history", rapid.SampleCount == 1);
    Check("Rapid current value is still evaluated", rapid.CurrentCpuPercent == 90);
    Check("One-minute observation advances history", distinct.SampleCount == 2);
    Check("Baseline is not established from rapid checks", !distinct.IsEstablished);

    string restartPath = Path.Combine(root, "restart.json");
    var beforeRestart = new PerformanceBaselineService(restartPath);
    DateTime restartStart = DateTime.UtcNow.AddMinutes(-2);
    beforeRestart.Record(Snapshot(restartStart, 15));
    beforeRestart.Record(Snapshot(restartStart.AddMinutes(1), 25));

    var afterRestart = new PerformanceBaselineService(restartPath);
    var restored = afterRestart.GetCurrent();
    Check("Accepted baseline history persists across restart", restored.SampleCount == 2);
    Check("Restart preserves latest accepted baseline value", restored.CurrentCpuPercent == 25);

    string corruptPath = Path.Combine(root, "corrupt.json");
    File.WriteAllText(corruptPath, "{ definitely-not-json");
    var corrupt = new PerformanceBaselineService(corruptPath);
    Check("Corrupt persisted baseline fails closed to relearning", corrupt.GetCurrent().SampleCount == 0);

    string futurePath = Path.Combine(root, "future.json");
    WritePersistedFixture(futurePath, DateTime.UtcNow.AddHours(1), cpu: 80);
    var future = new PerformanceBaselineService(futurePath);
    Check("Far-future persisted sample is rejected", future.GetCurrent().SampleCount == 0);
    var futureRecovery = future.Record(Snapshot(DateTime.UtcNow, 30));
    Check("Rejected future history cannot suppress new legitimate samples", futureRecovery.SampleCount == 1);

    string stalePath = Path.Combine(root, "stale.json");
    WritePersistedFixture(stalePath, DateTime.UtcNow.AddDays(-2), cpu: 70);
    var stale = new PerformanceBaselineService(stalePath);
    Check("Stale persisted history is discarded", stale.GetCurrent().SampleCount == 0);

    string nonFinitePath = Path.Combine(root, "nonfinite.json");
    var finite = new PerformanceBaselineService(nonFinitePath);
    var sanitized = finite.Record(new SystemSnapshot
    {
        Timestamp = DateTime.UtcNow,
        CpuUsagePercent = double.NaN,
        MemoryUsagePercent = double.PositiveInfinity,
        DiskUsagePercent = double.NegativeInfinity,
        ProcessCount = -4,
        DownloadMbps = double.NaN,
        UploadMbps = double.PositiveInfinity
    });
    Check("Non-finite live metrics are sanitized", sanitized.CurrentCpuPercent == 0 && sanitized.CurrentMemoryPercent == 0 && sanitized.CurrentDiskUsedPercent == 0 && sanitized.CurrentProcessCount == 0);
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
Environment.ExitCode = failures == 0 ? 0 : 1;

static void WritePersistedFixture(string path, DateTime timestamp, double cpu)
{
    var payload = new
    {
        SchemaVersion = 1,
        Samples = new[]
        {
            new
            {
                Timestamp = timestamp,
                CpuPercent = cpu,
                MemoryPercent = 40d,
                DiskUsedPercent = 50d,
                ProcessCount = 100,
                DownloadMbps = 5d,
                UploadMbps = 2d
            }
        }
    };

    File.WriteAllText(path, JsonSerializer.Serialize(payload));
}
