using System.Runtime.CompilerServices;

internal static class CleanupFailureAcceptance
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;
        VerifyRestoreCleanupFailure();
        VerifyDeleteCleanupFailure();
    }

    private static void VerifyRestoreCleanupFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelAI-QuarantineCleanupFailure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FileStream? recordLock = null;
        try
        {
            string sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            string source = Path.Combine(sourceDir, "restore-cleanup.txt");
            File.WriteAllText(source, "restore-cleanup-evidence");
            string itemId = Guid.NewGuid().ToString("N");

            var setup = Engine(root);
            setup.EnsureDirectories();
            Require(setup.Quarantine(itemId, source).Succeeded, "Restore cleanup fixture quarantine failed.");

            var restoring = Engine(root, checkpoint =>
            {
                if (checkpoint == QuarantineCheckpoint.RestorePayloadDeleted)
                {
                    recordLock = new FileStream(
                        setup.RecordPathForTest(itemId),
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                }
            });

            QuarantineStoreResult result = restoring.Restore(itemId);
            Require(!result.Succeeded && result.Code == "MetadataCleanupFailed",
                $"Restore cleanup failure was reported incorrectly: {result.Code} {result.Message}");
            Require(File.Exists(source), "Verified restore destination was lost after metadata cleanup failure.");
            Require(!File.Exists(setup.PayloadPathForTest(itemId)), "Protected payload remained after its verified restore/delete step.");
            Require(File.Exists(setup.RecordPathForTest(itemId)), "Locked protected record was unexpectedly removed.");
            Require(File.Exists(setup.TransactionPathForTest(itemId)), "Restore transaction was erased even though record cleanup failed.");
        }
        finally
        {
            recordLock?.Dispose();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void VerifyDeleteCleanupFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelAI-QuarantineCleanupFailure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FileStream? recordLock = null;
        try
        {
            string sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            string source = Path.Combine(sourceDir, "delete-cleanup.txt");
            File.WriteAllText(source, "delete-cleanup-evidence");
            string itemId = Guid.NewGuid().ToString("N");

            var setup = Engine(root);
            setup.EnsureDirectories();
            Require(setup.Quarantine(itemId, source).Succeeded, "Delete cleanup fixture quarantine failed.");

            var deleting = Engine(root, checkpoint =>
            {
                if (checkpoint == QuarantineCheckpoint.DeletePayloadDeleted)
                {
                    recordLock = new FileStream(
                        setup.RecordPathForTest(itemId),
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                }
            });

            QuarantineStoreResult result = deleting.Delete(itemId);
            Require(!result.Succeeded && result.Code == "MetadataCleanupFailed",
                $"Delete cleanup failure was reported incorrectly: {result.Code} {result.Message}");
            Require(!File.Exists(setup.PayloadPathForTest(itemId)), "Protected payload remained after verified permanent deletion.");
            Require(File.Exists(setup.RecordPathForTest(itemId)), "Locked protected record was unexpectedly removed.");
            Require(File.Exists(setup.TransactionPathForTest(itemId)), "Delete transaction was erased even though record cleanup failed.");
        }
        finally
        {
            recordLock?.Dispose();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static QuarantineStoreEngine Engine(string root, Action<QuarantineCheckpoint>? checkpoint = null) =>
        new(root, _ => false, _ => { }, _ => { }, checkpoint);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
