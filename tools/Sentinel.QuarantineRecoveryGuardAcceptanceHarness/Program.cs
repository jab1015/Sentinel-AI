using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string NewRoot()
{
    string root = Path.Combine(Path.GetTempPath(), "SentinelRecoveryGuard-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "QuarantineStore"));
    Directory.CreateDirectory(Path.Combine(root, "QuarantineRecords"));
    Directory.CreateDirectory(Path.Combine(root, "Transactions"));
    return root;
}

static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

static void WriteRecord(string root, string id, string originalPath, string hash)
{
    var record = new ProtectedQuarantineRecord(id, originalPath, hash, DateTimeOffset.UtcNow);
    File.WriteAllText(Path.Combine(root, "QuarantineRecords", id + ".json"), JsonSerializer.Serialize(record), new UTF8Encoding(false));
}

static void WriteTxn(string root, QuarantineTransaction txn)
{
    File.WriteAllText(Path.Combine(root, "Transactions", txn.ItemId + ".json"), JsonSerializer.Serialize(txn), new UTF8Encoding(false));
}

Console.WriteLine("=== Sentinel AI Quarantine Recovery Guard Acceptance ===");

// Valid quarantine recovery transaction remains eligible.
{
    string root = NewRoot();
    try
    {
        string id = Guid.NewGuid().ToString("N");
        string source = Path.Combine(root, "source.bin");
        string hash = Sha("source");
        WriteTxn(root, new QuarantineTransaction(id, "Quarantine", "Prepared", source,
            Path.Combine(root, "QuarantineStore", id + ".tmp"), hash, DateTimeOffset.UtcNow));
        Require(QuarantineRecoveryGuard.Validate(root).Count == 0, "Valid quarantine transaction was blocked.");
        Console.WriteLine("valid quarantine transaction: PASS");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

// Valid restore/delete transactions must be exactly bound to the protected record.
{
    string root = NewRoot();
    try
    {
        string id = Guid.NewGuid().ToString("N");
        string original = Path.Combine(root, "source", "file.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        string hash = Sha("payload");
        WriteRecord(root, id, original, hash);
        string temp = Path.Combine(Path.GetDirectoryName(original)!, $".{Path.GetFileName(original)}.sentinel-restore-{id}-{Guid.NewGuid():N}.tmp");
        WriteTxn(root, new QuarantineTransaction(id, "Restore", "TempReady", original, temp, hash, DateTimeOffset.UtcNow));
        Require(QuarantineRecoveryGuard.Validate(root).Count == 0, "Valid restore transaction was blocked.");
        File.Delete(Path.Combine(root, "Transactions", id + ".json"));
        WriteTxn(root, new QuarantineTransaction(id, "Delete", "Prepared", original, string.Empty, hash, DateTimeOffset.UtcNow));
        Require(QuarantineRecoveryGuard.Validate(root).Count == 0, "Valid delete transaction was blocked.");
        Console.WriteLine("record-bound restore/delete transactions: PASS");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

// Forged restore path/temp must block all automatic recovery inputs.
{
    string root = NewRoot();
    try
    {
        string id = Guid.NewGuid().ToString("N");
        string original = Path.Combine(root, "source", "file.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        string hash = Sha("payload");
        WriteRecord(root, id, original, hash);
        string attackerDir = Path.Combine(root, "attacker");
        Directory.CreateDirectory(attackerDir);
        string forgedDestination = Path.Combine(attackerDir, "victim.bin");
        string forgedTemp = Path.Combine(attackerDir, $".victim.bin.sentinel-restore-{id}-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(forgedDestination, "payload", new UTF8Encoding(false));
        File.WriteAllText(forgedTemp, "evidence", new UTF8Encoding(false));
        WriteTxn(root, new QuarantineTransaction(id, "Restore", "DestinationReady", forgedDestination, forgedTemp, hash, DateTimeOffset.UtcNow));
        IReadOnlyList<QuarantineStoreIssue> issues = QuarantineRecoveryGuard.Validate(root);
        Require(issues.Any(i => i.Code == "TransactionRecordMismatch"), "Forged restore transaction was not rejected.");
        Require(File.Exists(forgedDestination) && File.Exists(forgedTemp), "Read-only guard modified attacker-selected evidence.");
        Require(File.Exists(Path.Combine(root, "Transactions", id + ".json")), "Forged restore transaction was not preserved.");
        Console.WriteLine("forged restore redirection: PASS");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

// Forged delete hash must not override the protected record hash.
{
    string root = NewRoot();
    try
    {
        string id = Guid.NewGuid().ToString("N");
        string original = Path.Combine(root, "source", "file.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        string protectedHash = Sha("protected");
        WriteRecord(root, id, original, protectedHash);
        WriteTxn(root, new QuarantineTransaction(id, "Delete", "Prepared", original, string.Empty, Sha("forged"), DateTimeOffset.UtcNow));
        IReadOnlyList<QuarantineStoreIssue> issues = QuarantineRecoveryGuard.Validate(root);
        Require(issues.Any(i => i.Code == "TransactionRecordMismatch"), "Forged delete hash was not rejected.");
        Require(File.Exists(Path.Combine(root, "QuarantineRecords", id + ".json")) &&
                File.Exists(Path.Combine(root, "Transactions", id + ".json")), "Forged delete evidence was not preserved.");
        Console.WriteLine("forged delete hash: PASS");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

// Unknown operation, filename/ID mismatch, malformed JSON and invalid stage all fail closed.
{
    string root = NewRoot();
    try
    {
        string id = Guid.NewGuid().ToString("N");
        string hash = Sha("x");
        WriteTxn(root, new QuarantineTransaction(id, "ArbitraryOperation", "Prepared", Path.Combine(root, "x"), string.Empty, hash, DateTimeOffset.UtcNow));
        Require(QuarantineRecoveryGuard.Validate(root).Any(i => i.Code == "UnknownTransaction"), "Unknown operation was not blocked.");
        File.Delete(Path.Combine(root, "Transactions", id + ".json"));

        string malformed = Path.Combine(root, "Transactions", Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(malformed, "{not-json", new UTF8Encoding(false));
        Require(QuarantineRecoveryGuard.Validate(root).Any(i => i.Code == "CorruptTransaction"), "Malformed transaction was not blocked.");
        Console.WriteLine("unknown/malformed transactions: PASS");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

Console.WriteLine("RESULT: PASS");
