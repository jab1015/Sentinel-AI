using Sentinel.App.Services;

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

string root = Path.Combine(Path.GetTempPath(), "Sentinel-A28-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string path = Path.Combine(root, "state.json");
    OptimizationRuntimeStateStore store = new(path);
    Require(store.TryLoad(out OptimizationRuntimeState empty) && empty.LastAttemptUtc is null, "missing state is safe first-run empty state");

    DateTimeOffset attempt = DateTimeOffset.UtcNow;
    OptimizationRuntimeState expected = new(attempt, null, "reserved");
    Require(store.TrySave(expected), "reservation persists and verifies");
    Require(store.TryLoad(out OptimizationRuntimeState loaded) && loaded == expected, "persisted reservation reloads exactly");

    DateTimeOffset success = attempt;
    OptimizationRuntimeState completed = new(attempt, success, "completed");
    Require(store.TrySave(completed), "completed state persists and verifies all fields");
    Require(store.TryLoad(out OptimizationRuntimeState reloadedCompleted) && reloadedCompleted == completed, "completed state reloads exactly");

    File.WriteAllText(path, "{not-json");
    Require(!store.TryLoad(out _), "corrupt state fails closed");

    File.WriteAllText(path, "{}");
    Require(!store.TryLoad(out _), "existing empty object fails closed instead of erasing cooldown history");

    File.WriteAllText(path, "{\"LastSucceededUtc\":\"2026-09-12T12:00:00+00:00\",\"LastSummary\":\"invalid\"}");
    Require(!store.TryLoad(out _), "success without an attempt fails closed");

    File.WriteAllText(path, "{\"LastAttemptUtc\":\"2026-09-12T12:00:00+00:00\",\"LastSucceededUtc\":\"2026-09-12T12:01:00+00:00\",\"LastSummary\":\"invalid\"}");
    Require(!store.TryLoad(out _), "success newer than attempt fails closed");

    File.WriteAllText(path, new string('x', 64 * 1024 + 1));
    Require(!store.TryLoad(out _), "oversized persisted state fails closed before unbounded parsing");

    Require(store.TrySave(expected), "valid state can be restored after malformed-state tests");
    using (FileStream locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        Require(!store.TryLoad(out _), "locked state fails closed");
        Require(!store.TrySave(new OptimizationRuntimeState(DateTimeOffset.UtcNow, null, "locked")), "locked destination cannot report durable save success");
    }

    Require(store.TryAcquireExecutionLease(out IDisposable? firstLease) && firstLease is not null, "first process acquires optimization execution lease");
    try
    {
        Require(!store.TryAcquireExecutionLease(out IDisposable? secondLease) && secondLease is null, "second process cannot acquire concurrent optimization execution lease");
    }
    finally
    {
        firstLease?.Dispose();
    }

    Require(store.TryAcquireExecutionLease(out IDisposable? reacquiredLease) && reacquiredLease is not null, "execution lease can be reacquired after owner releases it");
    reacquiredLease?.Dispose();
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

Console.WriteLine("Sentinel optimization persistence A28 acceptance harness passed.");
