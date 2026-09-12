using System.Text.Json;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string NewRoot() => Path.Combine(Path.GetTempPath(), "SentinelAI-QuarantineAdversarial", Guid.NewGuid().ToString("N"));

static QuarantineStoreEngine Engine(
    string root,
    Action<QuarantineCheckpoint>? checkpoint = null,
    Action<string>? restoreInheritedAcl = null) =>
    new(root, _ => false, _ => { }, restoreInheritedAcl ?? (_ => { }), checkpoint);

static string NewItemId() => Guid.NewGuid().ToString("N");

static string WriteSource(string root, string name, string content)
{
    string sourceDir = Path.Combine(root, "source");
    Directory.CreateDirectory(sourceDir);
    string path = Path.Combine(sourceDir, name);
    File.WriteAllText(path, content);
    return path;
}

static void Cleanup(string root)
{
    try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
}

static void Run(string name, Action test)
{
    Console.WriteLine($"--- {name} ---");
    test();
    Console.WriteLine("PASS");
}

Console.WriteLine("=== Sentinel AI Quarantine Store Adversarial Acceptance ===");

Run("basic quarantine / restore", () =>
{
    string root = NewRoot();
    try
    {
        var engine = Engine(root); engine.EnsureDirectories();
        string source = WriteSource(root, "basic.txt", "sentinel-basic");
        string id = NewItemId();
        var q = engine.Quarantine(id, source);
        Require(q.Succeeded, $"Quarantine failed: {q.Code} {q.Message}");
        Require(!File.Exists(source) && File.Exists(engine.PayloadPathForTest(id)) && File.Exists(engine.RecordPathForTest(id)), "Quarantine did not commit expected protected state.");
        var r = engine.Restore(id);
        Require(r.Succeeded, $"Restore failed: {r.Code} {r.Message}");
        Require(File.Exists(source) && !File.Exists(engine.PayloadPathForTest(id)) && !File.Exists(engine.RecordPathForTest(id)), "Restore did not complete exact reversal.");
    }
    finally { Cleanup(root); }
});

Run("duplicate item ID rejected", () =>
{
    string root = NewRoot();
    try
    {
        var engine = Engine(root); engine.EnsureDirectories();
        string first = WriteSource(root, "first.txt", "first");
        string second = WriteSource(root, "second.txt", "second");
        string id = NewItemId();
        Require(engine.Quarantine(id, first).Succeeded, "Initial quarantine failed.");
        var duplicate = engine.Quarantine(id, second);
        Require(!duplicate.Succeeded && duplicate.Code == "ItemAlreadyExists" && File.Exists(second), "Duplicate item ID was not rejected safely.");
    }
    finally { Cleanup(root); }
});

Run("payload tampering blocks restore and delete", () =>
{
    string root = NewRoot();
    try
    {
        var engine = Engine(root); engine.EnsureDirectories();
        string source = WriteSource(root, "tamper.txt", "original");
        string id = NewItemId();
        Require(engine.Quarantine(id, source).Succeeded, "Quarantine failed.");
        File.AppendAllText(engine.PayloadPathForTest(id), "-tampered");
        var restore = engine.Restore(id);
        Require(!restore.Succeeded && restore.Code == "PayloadTampered", "Tampered payload was not refused on restore.");
        var delete = engine.Delete(id);
        Require(!delete.Succeeded && delete.Code == "PayloadTampered", "Tampered payload was not refused on permanent delete.");
        Require(File.Exists(engine.PayloadPathForTest(id)) && File.Exists(engine.RecordPathForTest(id)), "Tampered evidence was silently discarded.");
    }
    finally { Cleanup(root); }
});

Run("restore collision preserves quarantine", () =>
{
    string root = NewRoot();
    try
    {
        var engine = Engine(root); engine.EnsureDirectories();
        string source = WriteSource(root, "collision.txt", "quarantined");
        string id = NewItemId();
        Require(engine.Quarantine(id, source).Succeeded, "Quarantine failed.");
        File.WriteAllText(source, "new-owner-content");
        var restore = engine.Restore(id);
        Require(!restore.Succeeded && restore.Code == "RestoreCollision", "Existing restore destination was not rejected.");
        Require(File.ReadAllText(source) == "new-owner-content" && File.Exists(engine.PayloadPathForTest(id)), "Restore collision overwrote destination or discarded payload.");
    }
    finally { Cleanup(root); }
});

Run("restore ACL step holds exact destination against replacement", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "acl-race.txt", "trusted-restored-content");
        string replacement = Path.Combine(root, "attacker-replacement.txt");
        File.WriteAllText(replacement, "attacker-content");
        string id = NewItemId();
        var setup = Engine(root); setup.EnsureDirectories();
        Require(setup.Quarantine(id, source).Succeeded, "Quarantine failed.");

        bool replacementBlocked = false;
        var restoring = Engine(root, restoreInheritedAcl: destination =>
        {
            try
            {
                File.Move(replacement, destination, overwrite: true);
            }
            catch (IOException)
            {
                replacementBlocked = true;
            }
            catch (UnauthorizedAccessException)
            {
                replacementBlocked = true;
            }
        });

        var result = restoring.Restore(id);
        Require(result.Succeeded, $"Restore failed: {result.Code} {result.Message}");
        Require(replacementBlocked, "Restore destination could be replaced during the privileged ACL step.");
        Require(File.Exists(source) && File.ReadAllText(source) == "trusted-restored-content", "Restore destination identity/content changed during ACL processing.");
    }
    finally { Cleanup(root); }
});

Run("crash after quarantine payload commit rolls back safely", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "crash-before-delete.txt", "checkpoint-A");
        string id = NewItemId();
        var crashing = Engine(root, cp => { if (cp == QuarantineCheckpoint.QuarantinePayloadReady) throw new IOException("simulated crash"); });
        crashing.EnsureDirectories();
        try { crashing.Quarantine(id, source); throw new InvalidOperationException("Checkpoint did not fire."); } catch (IOException) { }
        var recovered = Engine(root); recovered.EnsureDirectories();
        var issues = recovered.Recover();
        Require(File.Exists(source), "Source was lost before exact deletion committed.");
        Require(!File.Exists(recovered.PayloadPathForTest(id)) && !File.Exists(recovered.TransactionPathForTest(id)), "Rollback left protected duplicate/transaction behind.");
        Require(issues.Count == 0, "Safe rollback unexpectedly reported unresolved recovery state.");
    }
    finally { Cleanup(root); }
});

Run("crash after source deletion commits safely", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "crash-after-delete.txt", "checkpoint-B");
        string id = NewItemId();
        var crashing = Engine(root, cp => { if (cp == QuarantineCheckpoint.QuarantineSourceDeleted) throw new IOException("simulated crash"); });
        crashing.EnsureDirectories();
        try { crashing.Quarantine(id, source); throw new InvalidOperationException("Checkpoint did not fire."); } catch (IOException) { }
        var recovered = Engine(root); recovered.EnsureDirectories();
        var issues = recovered.Recover();
        Require(!File.Exists(source), "Deleted source unexpectedly reappeared.");
        Require(File.Exists(recovered.PayloadPathForTest(id)) && File.Exists(recovered.RecordPathForTest(id)), "Recovery failed to commit protected record for contained payload.");
        Require(!File.Exists(recovered.TransactionPathForTest(id)) && issues.Count == 0, "Committed quarantine retained unresolved transaction state.");
    }
    finally { Cleanup(root); }
});

Run("crash after verified restore ACL commit finishes safely", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "restore-crash.txt", "checkpoint-C");
        string id = NewItemId();
        var engine = Engine(root); engine.EnsureDirectories();
        Require(engine.Quarantine(id, source).Succeeded, "Quarantine failed.");
        var crashing = Engine(root, cp => { if (cp == QuarantineCheckpoint.RestoreDestinationReady) throw new IOException("simulated crash"); });
        try { crashing.Restore(id); throw new InvalidOperationException("Checkpoint did not fire."); } catch (IOException) { }
        var recovered = Engine(root); recovered.EnsureDirectories();
        var issues = recovered.Recover();
        Require(File.Exists(source) && File.ReadAllText(source) == "checkpoint-C", "Verified restored destination was not preserved.");
        Require(!File.Exists(recovered.PayloadPathForTest(id)) && !File.Exists(recovered.RecordPathForTest(id)) && !File.Exists(recovered.TransactionPathForTest(id)), "Recovery did not finalize committed restore.");
        Require(issues.Count == 0, "Committed restore unexpectedly remained ambiguous.");
    }
    finally { Cleanup(root); }
});

Run("legacy destination-ready restore state fails closed", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "legacy-restore.txt", "legacy-content");
        string id = NewItemId();
        var engine = Engine(root); engine.EnsureDirectories();
        var q = engine.Quarantine(id, source);
        Require(q.Succeeded, "Quarantine failed.");
        File.WriteAllText(source, "legacy-content");
        File.WriteAllText(engine.TransactionPathForTest(id), JsonSerializer.Serialize(
            new QuarantineTransaction(id, "Restore", "DestinationReady", source, string.Empty, q.Sha256, DateTimeOffset.UtcNow)));

        var issues = engine.Recover();
        Require(File.Exists(source) && File.ReadAllText(source) == "legacy-content", "Legacy recovery altered the existing destination.");
        Require(File.Exists(engine.PayloadPathForTest(id)) && File.Exists(engine.RecordPathForTest(id)) && File.Exists(engine.TransactionPathForTest(id)),
            "Legacy DestinationReady state was auto-finalized without exact-object ACL proof.");
        Require(issues.Any(i => i.Code == "IncompleteRestore"), "Legacy DestinationReady state was not surfaced as recovery-required.");
    }
    finally { Cleanup(root); }
});

Run("destination-acl-ready recovery requires exact stable object", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "acl-ready.txt", "acl-ready-content");
        string id = NewItemId();
        var engine = Engine(root); engine.EnsureDirectories();
        var q = engine.Quarantine(id, source);
        Require(q.Succeeded, "Quarantine failed.");
        File.WriteAllText(source, "acl-ready-content");
        File.WriteAllText(engine.TransactionPathForTest(id), JsonSerializer.Serialize(
            new QuarantineTransaction(id, "Restore", "DestinationAclReady", source, string.Empty, q.Sha256, DateTimeOffset.UtcNow)));

        var issues = engine.Recover();
        Require(File.Exists(source) && File.ReadAllText(source) == "acl-ready-content", "Recovery altered the committed restored destination.");
        Require(!File.Exists(engine.PayloadPathForTest(id)) && !File.Exists(engine.RecordPathForTest(id)) && !File.Exists(engine.TransactionPathForTest(id)),
            "Verified DestinationAclReady state did not finalize protected cleanup.");
        Require(issues.Count == 0, "Verified DestinationAclReady recovery unexpectedly reported unresolved state.");
    }
    finally { Cleanup(root); }
});

Run("corrupt record is preserved", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "corrupt-record.txt", "record-evidence");
        string id = NewItemId();
        var engine = Engine(root); engine.EnsureDirectories();
        Require(engine.Quarantine(id, source).Succeeded, "Quarantine failed.");
        File.WriteAllText(engine.RecordPathForTest(id), "{not-json");
        var restore = engine.Restore(id);
        Require(!restore.Succeeded && restore.Code == "RecordCorrupt", "Corrupt record did not fail closed.");
        var issues = engine.Recover();
        Require(issues.Any(i => i.Code == "CorruptRecord"), "Corrupt record was not surfaced by recovery.");
        Require(File.Exists(engine.RecordPathForTest(id)) && File.Exists(engine.PayloadPathForTest(id)), "Corrupt protected evidence was discarded.");
    }
    finally { Cleanup(root); }
});

Run("tampered restore transaction cannot target arbitrary matching file", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "txn-path.txt", "same-content");
        string id = NewItemId();
        var engine = Engine(root); engine.EnsureDirectories();
        var q = engine.Quarantine(id, source);
        Require(q.Succeeded, "Quarantine failed.");
        string victimDir = Path.Combine(root, "victim"); Directory.CreateDirectory(victimDir);
        string victim = Path.Combine(victimDir, "unrelated.txt"); File.WriteAllText(victim, "same-content");
        File.WriteAllText(engine.TransactionPathForTest(id), JsonSerializer.Serialize(new QuarantineTransaction(id, "Restore", "Prepared", victim, string.Empty, q.Sha256, DateTimeOffset.UtcNow)));
        var issues = engine.Recover();
        Require(File.Exists(victim), "Recovery removed or altered an unrelated matching file.");
        Require(File.Exists(engine.PayloadPathForTest(id)) && File.Exists(engine.RecordPathForTest(id)), "Tampered transaction caused protected payload/record destruction.");
        Require(File.Exists(engine.TransactionPathForTest(id)), "Tampered transaction was silently discarded.");
        Require(issues.Count > 0, "Tampered transaction was not surfaced as recovery-required state.");
    }
    finally { Cleanup(root); }
});

Run("tampered restore temp path cannot delete arbitrary file", () =>
{
    string root = NewRoot();
    try
    {
        string source = WriteSource(root, "txn-temp.txt", "payload");
        string id = NewItemId();
        var engine = Engine(root); engine.EnsureDirectories();
        var q = engine.Quarantine(id, source);
        Require(q.Succeeded, "Quarantine failed.");
        string victimDir = Path.Combine(root, "elsewhere"); Directory.CreateDirectory(victimDir);
        string victim = Path.Combine(victimDir, $".anything.sentinel-restore-{id}-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(victim, "do-not-delete");
        File.WriteAllText(engine.TransactionPathForTest(id), JsonSerializer.Serialize(new QuarantineTransaction(id, "Restore", "Prepared", source, victim, q.Sha256, DateTimeOffset.UtcNow)));
        var issues = engine.Recover();
        Require(File.Exists(victim), "Recovery deleted an attacker-selected path outside the trusted restore directory.");
        Require(File.Exists(engine.PayloadPathForTest(id)) && File.Exists(engine.RecordPathForTest(id)), "Tampered temp path caused protected state loss.");
        Require(File.Exists(engine.TransactionPathForTest(id)), "Tampered transaction was silently discarded.");
        Require(issues.Count > 0, "Tampered temp path was not surfaced as recovery-required state.");
    }
    finally { Cleanup(root); }
});

Console.WriteLine("RESULT: PASS");