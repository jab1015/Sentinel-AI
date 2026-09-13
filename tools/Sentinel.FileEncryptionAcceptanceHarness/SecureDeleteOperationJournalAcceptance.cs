using Sentinel.App.Services;
using System.Security.Cryptography;

internal static class SecureDeleteOperationJournalAcceptance
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteJournalHarness", Guid.NewGuid().ToString("N"));
        string journalRoot = Path.Combine(root, "journal");
        Directory.CreateDirectory(root);
        try
        {
            string target = Path.Combine(root, "journal-target.bin");
            byte[] original = "secure delete journal target must remain untouched"u8.ToArray();
            File.WriteAllBytes(target, original);
            string originalHash = Convert.ToHexString(SHA256.HashData(original));

            SecureDeleteTargetValidationResult validated = SecureDeleteTargetValidator.Validate(target);
            Require(validated.Succeeded, "Journal fixture did not validate: " + validated.Code);

            SecureDeleteCoordinator coordinator = new();
            SecureDeletePreparationResult prepared = coordinator.Prepare(validated.Target);
            Require(prepared.Succeeded && prepared.Authorization is not null,
                "Journal fixture did not receive authorization: " + prepared.Code);

            SecureDeleteOperationJournal journal = new(journalRoot);
            SecureDeleteOperationRecord record = journal.Begin(prepared.Authorization!);
            Require(record.State == SecureDeleteOperationState.Prepared,
                "New journal operation did not start in Prepared state.");
            Require(record.Target == validated.Target && record.AuthorizationId == prepared.Authorization!.AuthorizationId,
                "Journal did not bind the exact approved target and authorization.");

            SecureDeleteOperationJournal reopened = new(journalRoot);
            SecureDeleteOperationRecord persisted = reopened.ReadRequired(record.OperationId);
            Require(persisted == record, "Journal did not survive reopen exactly.");

            persisted = reopened.Advance(persisted, SecureDeleteOperationState.IdentityVerified, "exact identity revalidated");
            Require(persisted.State == SecureDeleteOperationState.IdentityVerified,
                "Journal did not persist IdentityVerified.");

            bool skippedStateRejected = false;
            try
            {
                reopened.Advance(persisted, SecureDeleteOperationState.PrimaryRemovalVerified);
            }
            catch (InvalidOperationException)
            {
                skippedStateRejected = true;
            }
            Require(skippedStateRejected, "Journal accepted a skipped destructive-state transition.");

            SecureDeleteOperationRecord recovery = reopened.Advance(
                persisted,
                SecureDeleteOperationState.RecoveryRequired,
                "simulated ambiguous operation state");
            Require(recovery.State == SecureDeleteOperationState.RecoveryRequired,
                "Journal did not persist RecoveryRequired.");

            bool recoveryExitRejected = false;
            try
            {
                reopened.Advance(recovery, SecureDeleteOperationState.Complete);
            }
            catch (InvalidOperationException)
            {
                recoveryExitRejected = true;
            }
            Require(recoveryExitRejected, "RecoveryRequired was allowed to silently become Complete.");

            Require(File.Exists(target), "Journal operation removed the approved target.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) == originalHash,
                "Journal operation changed approved target content.");

            string journalPath = Path.Combine(journalRoot, record.OperationId.ToString("N") + ".json");
            string json = File.ReadAllText(journalPath);
            File.WriteAllText(journalPath, json.Replace("RecoveryRequired", "NotARealState", StringComparison.Ordinal));
            Require(!reopened.TryRead(record.OperationId, out _),
                "Journal accepted tampered/invalid state metadata.");

            Require(File.Exists(target), "Journal tamper handling affected the approved target.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) == originalHash,
                "Journal tamper handling changed approved target content.");

            Console.WriteLine("Secure Delete durable journal reopen / exact-target binding: PASS");
            Console.WriteLine("Secure Delete journal monotonic transition enforcement: PASS");
            Console.WriteLine("Secure Delete RecoveryRequired fail-closed semantics: PASS");
            Console.WriteLine("Secure Delete journal target non-mutation / tamper rejection: PASS");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
