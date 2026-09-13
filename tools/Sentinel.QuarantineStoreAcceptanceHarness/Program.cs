using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

static QuarantineStoreEngine CreateEngine(string root, Action<QuarantineCheckpoint>? checkpoint = null) =>
    new(
        root,
        path => false,
        path => { },
        path => { },
        checkpoint);

static string NewRoot()
{
    string root = Path.Combine(Path.GetTempPath(), "SentinelQuarantineAcceptance-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static string CreateSource(string root, string name, string content)
{
    string sourceDir = Path.Combine(root, "source");
    Directory.CreateDirectory(sourceDir);
    string path = Path.Combine(sourceDir, name);
    File.WriteAllText(path, content, new UTF8Encoding(false));
    return path;
}

static void Cleanup(string root)
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

Console.WriteLine("=== Sentinel AI QuarantineStore adversarial acceptance ===");

// Normal end-to-end quarantine/restore/delete behavior.
{
    string root = NewRoot();
    try
    {
        var engine = CreateEngine(root);
        engine.EnsureDirectories();
        string source = CreateSource(root, "normal.bin", "sentinel-normal");
        string id = Guid.NewGuid().ToString("N");
        QuarantineStoreResult quarantined = engine.Quarantine(id, source);
        Require(quarantined.Succeeded, $"Normal quarantine failed: {quarantined.Code} {quarantined.Message}");
        Require(!File.Exists(source), "Normal quarantine left the original source present.");
        Require(File.Exists(engine.PayloadPathForTest(id)), "Normal quarantine did not create a protected payload.");
        Require(File.Exists(engine.RecordPathForTest(id)), "Normal quarantine did not create a protected record.");

        QuarantineStoreResult restored = engine.Restore(id);
        Require(restored.Succeeded, $"Normal restore failed: {restored.Code} {restored.Message}");
        Require(File.Exists(source), "Normal restore did not recreate the original file.");
        Require(!File.Exists(engine.PayloadPathForTest(id)), "Normal restore left the protected payload behind.");
        Require(!File.Exists(engine.RecordPathForTest(id)), "Normal restore left the protected record behind.");

        string deleteSource = CreateSource(root, "delete.bin", "sentinel-delete");
        string deleteId = Guid.NewGuid().ToString("N");
        Require(engine.Quarantine(deleteId, deleteSource).Succeeded, "Delete fixture quarantine failed.");
        QuarantineStoreResult deleted = engine.Delete(deleteId);
        Require(deleted.Succeeded, $"Normal permanent delete failed: {deleted.Code} {deleted.Message}");
        Require(!File.Exists(engine.PayloadPathForTest(deleteId)), "Permanent delete left payload behind.");
        Require(!File.Exists(engine.RecordPathForTest(deleteId)), "Permanent delete left record behind.");
        Console.WriteLine("normal quarantine/restore/delete: PASS");
    }
    finally { Cleanup(root); }
}

// Invalid IDs and duplicate IDs must fail closed.
{
    string root = NewRoot();
    try
    {
        var engine = CreateEngine(root);
        engine.EnsureDirectories();
        string source = CreateSource(root, "id.bin", "id-check");
        Require(!engine.Quarantine("..\\escape", source).Succeeded, "Path-traversal-shaped item ID was accepted.");
        string id = Guid.NewGuid().ToString("N");
        Require(engine.Quarantine(id, source).Succeeded, "Initial duplicate-ID fixture quarantine failed.");
        string second = CreateSource(root, "id2.bin", "id-check-2");
        QuarantineStoreResult duplicate = engine.Quarantine(id, second);
        Require(!duplicate.Succeeded && duplicate.Code == "ItemAlreadyExists", "Duplicate item ID was not rejected.");
        Console.WriteLine("invalid/path-traversal and duplicate IDs: PASS");
    }
    finally { Cleanup(root); }
}

// Payload tampering must block restore/delete and preserve evidence.
{
    string root = NewRoot();
    try
    {
        var engine = CreateEngine(root);
        engine.EnsureDirectories();
        string source = CreateSource(root, "tamper.bin", "trusted-payload");
        string id = Guid.NewGuid().ToString("N");
        Require(engine.Quarantine(id, source).Succeeded, "Payload-tamper fixture quarantine failed.");
        File.WriteAllText(engine.PayloadPathForTest(id), "tampered-payload", new UTF8Encoding(false));
        QuarantineStoreResult restore = engine.Restore(id);
        Require(!restore.Succeeded && restore.Code == "PayloadTampered", "Tampered payload was not rejected during restore.");
        QuarantineStoreResult delete = engine.Delete(id);
        Require(!delete.Succeeded && delete.Code == "PayloadTampered", "Tampered payload was not rejected during delete.");
        Require(File.Exists(engine.PayloadPathForTest(id)) && File.Exists(engine.RecordPathForTest(id)), "Tampered evidence was discarded.");
        Console.WriteLine("payload tampering preserved: PASS");
    }
    finally { Cleanup(root); }
}

// Corrupt record and transaction files must be preserved and reported.
{
    string root = NewRoot();
    try
    {
        var engine = CreateEngine(root);
        engine.EnsureDirectories();
        string corruptRecord = Path.Combine(root, "QuarantineRecords", Guid.NewGuid().ToString("N") + ".json");
        string corruptTxn = Path.Combine(root, "Transactions", Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(corruptRecord, "{not-json", new UTF8Encoding(false));
        File.WriteAllText(corruptTxn, "{not-json", new UTF8Encoding(false));
        IReadOnlyList<QuarantineStoreIssue> issues = engine.Recover();
        Require(issues.Any(i => i.Code == "CorruptRecord"), "Corrupt record was not reported.");
        Require(issues.Any(i => i.Code == "CorruptTransaction"), "Corrupt transaction was not reported.");
        Require(File.Exists(corruptRecord) && File.Exists(corruptTxn), "Corrupt evidence was deleted during recovery.");
        Console.WriteLine("corrupt record/transaction preservation: PASS");
    }
    finally { Cleanup(root); }
}

// Crash after quarantine intent: safe rollback keeps source and removes transaction.
{
    string root = NewRoot();
    try
    {
        string source = CreateSource(root, "crash-intent.bin", "crash-intent");
        string id = Guid.NewGuid().ToString("N");
        var crashing = CreateEngine(root, cp => { if (cp == QuarantineCheckpoint.QuarantineIntentPersisted) throw new IOException("injected crash"); });
        crashing.EnsureDirectories();
        try { crashing.Quarantine(id, source); } catch (IOException) { }
        var recovered = CreateEngine(root);
        IReadOnlyList<QuarantineStoreIssue> issues = recovered.Recover();
        Require(File.Exists(source), "Intent-stage crash recovery lost the source.");
        Require(!File.Exists(recovered.PayloadPathForTest(id)), "Intent-stage crash recovery left a payload.");
        Require(!File.Exists(recovered.TransactionPathForTest(id)), "Intent-stage safe rollback left the transaction.");
        Require(issues.All(i => i.Code != "RecoveryFailed"), "Intent-stage recovery reported an unexpected failure.");
        Console.WriteLine("quarantine crash after intent: PASS");
    }
    finally { Cleanup(root); }
}

// Crash after source deletion: recovery must safely commit a verified protected record.
{
    string root = NewRoot();
    try
    {
        string source = CreateSource(root, "crash-delete.bin", "crash-after-delete");
        string id = Guid.NewGuid().ToString("N");
        var crashing = CreateEngine(root, cp => { if (cp == QuarantineCheckpoint.QuarantineSourceDeleted) throw new IOException("injected crash"); });
        crashing.EnsureDirectories();
        try { crashing.Quarantine(id, source); } catch (IOException) { }
        var recovered = CreateEngine(root);
        IReadOnlyList<QuarantineStoreIssue> issues = recovered.Recover();
        Require(!File.Exists(source), "Source unexpectedly reappeared after source-deleted crash.");
        Require(File.Exists(recovered.PayloadPathForTest(id)), "Recovery lost the protected payload after source deletion.");
        Require(File.Exists(recovered.RecordPathForTest(id)), "Recovery did not commit the protected record after verified source deletion.");
        Require(!File.Exists(recovered.TransactionPathForTest(id)), "Committed quarantine recovery left transaction state behind.");
        Require(issues.All(i => i.Code != "RecoveryFailed"), "Source-deleted recovery reported an unexpected failure.");
        Console.WriteLine("quarantine crash after source deletion: PASS");
    }
    finally { Cleanup(root); }
}

// Crash after restore destination commit: recovery must finish only from record-bound state.
{
    string root = NewRoot();
    try
    {
        string source = CreateSource(root, "restore-crash.bin", "restore-crash");
        string id = Guid.NewGuid().ToString("N");
        var setup = CreateEngine(root);
        setup.EnsureDirectories();
        Require(setup.Quarantine(id, source).Succeeded, "Restore-crash fixture quarantine failed.");
        var crashing = CreateEngine(root, cp => { if (cp == QuarantineCheckpoint.RestoreDestinationReady) throw new IOException("injected crash"); });
        try { crashing.Restore(id); } catch (IOException) { }
        var recovered = CreateEngine(root);
        IReadOnlyList<QuarantineStoreIssue> issues = recovered.Recover();
        Require(File.Exists(source), "Recovery lost a committed restore destination.");
        Require(!File.Exists(recovered.PayloadPathForTest(id)), "Recovery did not remove payload after verified restore commit.");
        Require(!File.Exists(recovered.RecordPathForTest(id)), "Recovery did not remove record after verified restore commit.");
        Require(!File.Exists(recovered.TransactionPathForTest(id)), "Recovery left restore transaction after verified commit.");
        Require(issues.All(i => i.Code != "RecoveryFailed"), "Restore recovery reported an unexpected failure.");
        Console.WriteLine("restore crash after destination commit: PASS");
    }
    finally { Cleanup(root); }
}

// Adversarial restore transaction: transaction JSON must never be allowed to redirect recovery.
{
    string root = NewRoot();
    try
    {
        var engine = CreateEngine(root);
        engine.EnsureDirectories();
        string source = CreateSource(root, "redirect.bin", "same-content");
        string id = Guid.NewGuid().ToString("N");
        QuarantineStoreResult q = engine.Quarantine(id, source);
        Require(q.Succeeded, "Restore-redirection fixture quarantine failed.");

        string attackerDir = Path.Combine(root, "attacker");
        Directory.CreateDirectory(attackerDir);
        string attackerDestination = Path.Combine(attackerDir, "victim.bin");
        File.WriteAllText(attackerDestination, "same-content", new UTF8Encoding(false));
        string attackerTemp = Path.Combine(attackerDir, $".victim.bin.sentinel-restore-{id}-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(attackerTemp, "attacker-temp", new UTF8Encoding(false));

        var forged = new QuarantineTransaction(id, "Restore", "DestinationReady", attackerDestination, attackerTemp, q.Sha256, DateTimeOffset.UtcNow);
        File.WriteAllText(engine.TransactionPathForTest(id), JsonSerializer.Serialize(forged), new UTF8Encoding(false));

        IReadOnlyList<QuarantineStoreIssue> issues = engine.Recover();
        Require(issues.Any(i => i.Code == "TransactionRecordMismatch"), "Forged restore transaction was not rejected as inconsistent with the protected record.");
        Require(File.Exists(attackerDestination), "Forged restore transaction caused an attacker-selected destination to be modified/deleted.");
        Require(File.Exists(attackerTemp), "Forged restore transaction caused an attacker-selected temp file to be deleted.");
        Require(File.Exists(engine.PayloadPathForTest(id)), "Forged restore transaction caused protected payload deletion.");
        Require(File.Exists(engine.RecordPathForTest(id)), "Forged restore transaction caused protected record deletion.");
        Require(File.Exists(engine.TransactionPathForTest(id)), "Forged restore transaction evidence was not preserved.");
        Console.WriteLine("restore transaction redirection rejected: PASS");
    }
    finally { Cleanup(root); }
}

// Adversarial delete transaction: transaction hash must remain bound to the protected record.
{
    string root = NewRoot();
    try
    {
        var engine = CreateEngine(root);
        engine.EnsureDirectories();
        string source = CreateSource(root, "delete-tamper.bin", "original-delete-content");
        string id = Guid.NewGuid().ToString("N");
        Require(engine.Quarantine(id, source).Succeeded, "Delete-tamper fixture quarantine failed.");
        string payload = engine.PayloadPathForTest(id);
        File.WriteAllText(payload, "changed-payload", new UTF8Encoding(false));
        string forgedHash = HashText("changed-payload");
        var forged = new QuarantineTransaction(id, "Delete", "Prepared", source, string.Empty, forgedHash, DateTimeOffset.UtcNow);
        File.WriteAllText(engine.TransactionPathForTest(id), JsonSerializer.Serialize(forged), new UTF8Encoding(false));

        IReadOnlyList<QuarantineStoreIssue> issues = engine.Recover();
        Require(issues.Any(i => i.Code == "TransactionRecordMismatch"), "Delete transaction hash mismatch was not rejected.");
        Require(File.Exists(payload), "Hash-forged delete transaction caused payload deletion.");
        Require(File.Exists(engine.RecordPathForTest(id)), "Hash-forged delete transaction caused record deletion.");
        Require(File.Exists(engine.TransactionPathForTest(id)), "Hash-forged delete transaction evidence was not preserved.");
        Console.WriteLine("delete transaction hash mismatch rejected: PASS");
    }
    finally { Cleanup(root); }
}

Console.WriteLine("RESULT: PASS");
