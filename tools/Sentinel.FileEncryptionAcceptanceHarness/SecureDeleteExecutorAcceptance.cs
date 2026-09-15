using Sentinel.App.Services;
using System.Runtime.CompilerServices;

internal static class SecureDeleteExecutorAcceptance
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        Console.WriteLine("--- SecureDeleteExecutorAcceptance: START ---");
        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteExecutor", Guid.NewGuid().ToString("N"));
        string journalRoot = Path.Combine(root, "journal");
        Directory.CreateDirectory(root);
        try
        {
            VerifyRemoval(root, journalRoot, "normal.txt", "normal payload");
            VerifyRemoval(root, journalRoot, "empty.bin", string.Empty);
            VerifyRemoval(root, journalRoot, "résumé-源.txt", "unicode payload");

            string readOnly = Path.Combine(root, "readonly.txt");
            File.WriteAllText(readOnly, "read-only payload");
            File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
            try
            {
                SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Validate(readOnly);
                Require(validation.Succeeded, "Read-only source did not pass non-destructive target validation.");
                SecureDeleteCoordinator coordinator = new();
                SecureDeletePreparationResult prepared = coordinator.Prepare(validation.Target);
                Require(prepared.Succeeded && prepared.Authorization is not null, "Read-only source preparation failed.");
                SecureDeleteExactObjectExecutor executor = new(coordinator, new SecureDeleteOperationJournal(journalRoot));
                SecureDeleteExecutionResult result = executor.Execute(prepared.Authorization);
                Require(!result.Succeeded || result.PrimaryRemoval == SecureDeletePrimaryRemovalStatus.Verified,
                    "Read-only execution reported success without verified primary removal.");
                if (!result.Succeeded)
                {
                    Require(result.PrimaryRemoval is SecureDeletePrimaryRemovalStatus.Failed or SecureDeletePrimaryRemovalStatus.Unknown,
                        "Read-only failure did not use fail-closed removal semantics.");
                }
            }
            finally
            {
                if (File.Exists(readOnly)) File.SetAttributes(readOnly, FileAttributes.Normal);
            }

            string expires = Path.Combine(root, "expired.txt");
            File.WriteAllText(expires, "expire me");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SecureDeleteCoordinator expiringCoordinator = new(() => now);
            SecureDeleteTargetValidationResult expiringValidation = SecureDeleteTargetValidator.Validate(expires);
            Require(expiringValidation.Succeeded, "Expiration fixture target validation failed.");
            SecureDeletePreparationResult expiringPrepared = expiringCoordinator.Prepare(expiringValidation.Target);
            Require(expiringPrepared.Succeeded && expiringPrepared.Authorization is not null, "Expiration fixture preparation failed.");
            now = now.Add(SecureDeleteCoordinator.AuthorizationLifetime).AddSeconds(1);
            SecureDeleteExactObjectExecutor expiringExecutor = new(expiringCoordinator, new SecureDeleteOperationJournal(journalRoot));
            SecureDeleteExecutionResult expired = expiringExecutor.Execute(expiringPrepared.Authorization);
            Require(!expired.Succeeded && File.Exists(expires), "Expired authorization was permitted to mutate the target.");

            string replace = Path.Combine(root, "replacement.txt");
            File.WriteAllText(replace, "original");
            SecureDeleteTargetValidationResult original = SecureDeleteTargetValidator.Validate(replace);
            Require(original.Succeeded, "Replacement fixture validation failed.");
            SecureDeleteCoordinator replacementCoordinator = new();
            SecureDeletePreparationResult replacementPrepared = replacementCoordinator.Prepare(original.Target);
            Require(replacementPrepared.Succeeded && replacementPrepared.Authorization is not null, "Replacement fixture preparation failed.");
            File.Delete(replace);
            File.WriteAllText(replace, "replacement must survive");
            SecureDeleteExactObjectExecutor replacementExecutor = new(replacementCoordinator, new SecureDeleteOperationJournal(journalRoot));
            SecureDeleteExecutionResult replacementResult = replacementExecutor.Execute(replacementPrepared.Authorization);
            Require(!replacementResult.Succeeded, "A replaced target was accepted for Secure Delete.");
            Require(File.Exists(replace) && File.ReadAllText(replace) == "replacement must survive",
                "Secure Delete touched a replacement object outside the original authorization.");

            Console.WriteLine("Exact-handle normal/empty/Unicode removal: PASS");
            Console.WriteLine("Read-only behavior remains fail-closed: PASS");
            Console.WriteLine("Expired authorization rejection: PASS");
            Console.WriteLine("Pre-mutation replacement rejection: PASS");
            Console.WriteLine("RelatedCleanupPending / media CANNOT_PROVE semantics: PASS");
            Console.WriteLine("--- SecureDeleteExecutorAcceptance: COMPLETE ---");
        }
        finally
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch { }
        }
    }

    private static void VerifyRemoval(string root, string journalRoot, string fileName, string contents)
    {
        string path = Path.Combine(root, fileName);
        File.WriteAllText(path, contents);
        SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Validate(path);
        Require(validation.Succeeded, fileName + " target validation failed: " + validation.Message);

        SecureDeleteCoordinator coordinator = new();
        SecureDeletePreparationResult prepared = coordinator.Prepare(validation.Target);
        Require(prepared.Succeeded && prepared.Authorization is not null, fileName + " preparation failed.");

        SecureDeleteOperationJournal journal = new(journalRoot);
        SecureDeleteExactObjectExecutor executor = new(coordinator, journal);
        SecureDeleteExecutionResult result = executor.Execute(prepared.Authorization);

        Require(result.Succeeded, fileName + " exact-object logical removal failed: " + result.Code);
        Require(result.PrimaryRemoval == SecureDeletePrimaryRemovalStatus.Verified, fileName + " removal was not VERIFIED.");
        Require(result.JournalState == SecureDeleteOperationState.RelatedCleanupPending, fileName + " was prematurely marked complete.");
        Require(result.RelatedCleanupRequired, fileName + " did not require related discovery/cleanup.");
        Require(result.MediaAction == SecureDeleteMediaActionStatus.NotSupported, fileName + " incorrectly claimed media sanitization.");
        Require(result.PhysicalMediaAbsence == SecureDeletePhysicalMediaAbsenceStatus.CannotProve,
            fileName + " incorrectly claimed physical-media absence.");
        Require(!File.Exists(path), fileName + " remains at the original path after verified removal.");

        SecureDeleteOperationRecord persisted = journal.ReadRequired(result.OperationId);
        Require(persisted.State == SecureDeleteOperationState.RelatedCleanupPending,
            fileName + " durable journal did not persist RelatedCleanupPending.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
