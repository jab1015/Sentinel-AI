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
    Require(store.TryLoad(out OptimizationRuntimeState empty) && empty.LastAttemptUtc is null, "missing state is safe empty state");

    DateTimeOffset attempt = DateTimeOffset.UtcNow;
    OptimizationRuntimeState expected = new(attempt, null, "reserved");
    Require(store.TrySave(expected), "reservation persists and verifies");
    Require(store.TryLoad(out OptimizationRuntimeState loaded) && loaded.LastAttemptUtc == attempt, "persisted reservation reloads exactly");

    File.WriteAllText(path, "{not-json");
    Require(!store.TryLoad(out _), "corrupt state fails closed");

    File.WriteAllText(path, "{}");
    Require(store.TryLoad(out OptimizationRuntimeState defaults) && defaults.LastAttemptUtc is null, "valid legacy/default state remains readable");

    using (FileStream locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        Require(!store.TryLoad(out _), "locked state fails closed");
        Require(!store.TrySave(new OptimizationRuntimeState(DateTimeOffset.UtcNow, null, "locked")), "locked destination cannot report durable save success");
    }
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

Console.WriteLine("Sentinel optimization persistence A28 acceptance harness passed.");
