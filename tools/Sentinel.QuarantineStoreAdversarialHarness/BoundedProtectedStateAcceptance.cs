using System.Runtime.CompilerServices;

internal static class BoundedProtectedStateAcceptance
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelAI-QuarantineBoundedState", Guid.NewGuid().ToString("N"));
        try
        {
            string records = Path.Combine(root, "QuarantineRecords");
            string transactions = Path.Combine(root, "Transactions");
            Directory.CreateDirectory(records);
            Directory.CreateDirectory(transactions);

            string id = Guid.NewGuid().ToString("N");
            string transactionPath = Path.Combine(transactions, id + ".json");
            File.WriteAllBytes(transactionPath, new byte[BoundedProtectedJsonFile.MaximumBytes + 1]);

            IReadOnlyList<QuarantineStoreIssue> transactionIssues = QuarantineRecoveryGuard.Validate(root);
            Require(transactionIssues.Any(issue => issue.Code == "CorruptTransaction" && issue.Path == transactionPath),
                "Oversized quarantine transaction did not fail closed before parsing.");
            Require(File.Exists(transactionPath), "Oversized quarantine transaction evidence was not preserved.");

            File.Delete(transactionPath);
            string hash = new string('A', 64);
            string source = Path.Combine(root, "source.txt");
            string recordPath = Path.Combine(records, id + ".json");
            File.WriteAllBytes(recordPath, new byte[BoundedProtectedJsonFile.MaximumBytes + 1]);
            File.WriteAllText(transactionPath,
                System.Text.Json.JsonSerializer.Serialize(new QuarantineTransaction(
                    id,
                    "Restore",
                    "Prepared",
                    source,
                    Path.Combine(root, ".source.txt.sentinel-restore-" + id + "-123.tmp"),
                    hash,
                    DateTimeOffset.UtcNow)));

            IReadOnlyList<QuarantineStoreIssue> recordIssues = QuarantineRecoveryGuard.Validate(root);
            Require(recordIssues.Any(issue => issue.Code == "TransactionRecordMismatch"),
                "Oversized protected quarantine record did not block record-bound recovery.");
            Require(File.Exists(recordPath) && File.Exists(transactionPath),
                "Oversized protected-state evidence was discarded during fail-closed validation.");

            File.Delete(recordPath);
            File.Delete(transactionPath);

            string aclReadyId = Guid.NewGuid().ToString("N");
            string destination = Path.Combine(root, "restore-target.txt");
            string aclReadyRecordPath = Path.Combine(records, aclReadyId + ".json");
            string aclReadyTransactionPath = Path.Combine(transactions, aclReadyId + ".json");
            string aclReadyTemp = Path.Combine(root, ".restore-target.txt.sentinel-restore-" + aclReadyId + "-123.tmp");
            File.WriteAllText(aclReadyRecordPath,
                System.Text.Json.JsonSerializer.Serialize(new ProtectedQuarantineRecord(
                    aclReadyId,
                    destination,
                    hash,
                    DateTimeOffset.UtcNow)));
            File.WriteAllText(aclReadyTransactionPath,
                System.Text.Json.JsonSerializer.Serialize(new QuarantineTransaction(
                    aclReadyId,
                    "Restore",
                    "DestinationAclReady",
                    destination,
                    aclReadyTemp,
                    hash,
                    DateTimeOffset.UtcNow)));

            IReadOnlyList<QuarantineStoreIssue> aclReadyIssues = QuarantineRecoveryGuard.Validate(root);
            Require(!aclReadyIssues.Any(issue => issue.Path == aclReadyTransactionPath),
                "The durable DestinationAclReady restore stage was rejected by the recovery guard.");

            Console.WriteLine("Bounded protected quarantine transaction read: PASS");
            Console.WriteLine("Bounded protected quarantine record read: PASS");
            Console.WriteLine("ACL-ready restore recovery guard stage: PASS");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
