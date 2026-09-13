using Sentinel.App.Services;

internal static class SecureDeleteRecoveryExtendedAcceptance
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteRecoveryExtendedHarness", Guid.NewGuid().ToString("N"));
        string journalRoot = Path.Combine(root, "journal");
        Directory.CreateDirectory(root);
        try
        {
            VerifyCompleteStateContradiction(root, journalRoot);
            VerifyTimestampTamperFailsClosed(root, journalRoot);
            VerifyUnknownOperationFailsClosed(journalRoot);

            Console.WriteLine("Secure Delete complete-state contradiction fails closed: PASS");
            Console.WriteLine("Secure Delete timestamp-tamper recovery fails closed: PASS");
            Console.WriteLine("Secure Delete unknown-operation recovery fails closed: PASS");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void VerifyCompleteStateContradiction(string root, string journalRoot)
    {
        string path = Path.Combine(root, "complete-contradiction.bin");
        File.WriteAllText(path, "complete journal must not override live exact-object evidence");
        string original = File.ReadAllText(path);

        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "complete-contradiction"));
        SecureDeleteOperationRecord record = Begin(journal, path);
        record = journal.Advance(record, SecureDeleteOperationState.IdentityVerified);
        record = journal.Advance(record, SecureDeleteOperationState.PrimaryMutationStarted);
        record = journal.Advance(record, SecureDeleteOperationState.PrimaryRemovalVerified);
        record = journal.Advance(record, SecureDeleteOperationState.RelatedCleanupPending);
        record = journal.Advance(record, SecureDeleteOperationState.Complete);

        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(assessment.Phase == SecureDeleteRecoveryPhase.Complete &&
                assessment.TargetObservation == SecureDeleteRecoveryTargetObservation.ExactOriginalPresent &&
                assessment.RequiresUserAttention && !assessment.MayRequestFreshPreMutationAuthorization &&
                !assessment.MayAutomaticallyResumeMutation,
            "Recovery trusted Complete despite the exact original object still being present.");
        Require(File.ReadAllText(path) == original,
            "Complete-state contradiction recovery changed the exact original target.");
    }

    private static void VerifyTimestampTamperFailsClosed(string root, string journalRoot)
    {
        string path = Path.Combine(root, "timestamp-tamper.bin");
        File.WriteAllText(path, "timestamp tamper target must remain unchanged");
        string original = File.ReadAllText(path);
        string scopedJournalRoot = Path.Combine(journalRoot, "timestamp-tamper");

        SecureDeleteOperationJournal journal = new(scopedJournalRoot);
        SecureDeleteOperationRecord record = Begin(journal, path);
        string journalPath = Path.Combine(scopedJournalRoot, record.OperationId.ToString("N") + ".json");
        string authentic = File.ReadAllText(journalPath);
        string originalTimestamp = record.UpdatedUtc.ToString("O");
        string rolledBackTimestamp = record.UpdatedUtc.AddDays(-1).ToString("O");
        string tampered = authentic.Replace(originalTimestamp, rolledBackTimestamp, StringComparison.Ordinal);
        Require(!string.Equals(authentic, tampered, StringComparison.Ordinal),
            "Timestamp tamper fixture did not alter the authenticated journal.");
        File.WriteAllText(journalPath, tampered);

        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(assessment.Phase == SecureDeleteRecoveryPhase.JournalInvalid &&
                assessment.TargetObservation == SecureDeleteRecoveryTargetObservation.Unknown &&
                assessment.Record is null && assessment.RequiresUserAttention &&
                !assessment.MayRequestFreshPreMutationAuthorization && !assessment.MayAutomaticallyResumeMutation,
            "Recovery accepted a journal with rolled-back authenticated timestamp data.");
        Require(File.ReadAllText(path) == original,
            "Timestamp-tamper recovery changed the approved target.");
    }

    private static void VerifyUnknownOperationFailsClosed(string journalRoot)
    {
        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "unknown-operation"));
        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(Guid.NewGuid());
        Require(assessment.Phase == SecureDeleteRecoveryPhase.JournalInvalid &&
                assessment.TargetObservation == SecureDeleteRecoveryTargetObservation.Unknown &&
                assessment.Record is null && assessment.RequiresUserAttention &&
                !assessment.MayRequestFreshPreMutationAuthorization && !assessment.MayAutomaticallyResumeMutation,
            "Unknown recovery operation did not fail closed.");
    }

    private static SecureDeleteOperationRecord Begin(SecureDeleteOperationJournal journal, string path)
    {
        SecureDeleteTargetValidationResult validated = SecureDeleteTargetValidator.Validate(path);
        Require(validated.Succeeded, "Extended recovery fixture failed validation: " + validated.Code);
        SecureDeletePreparationResult prepared = new SecureDeleteCoordinator().Prepare(validated.Target);
        Require(prepared.Succeeded && prepared.Authorization is not null,
            "Extended recovery fixture failed authorization: " + prepared.Code);
        return journal.Begin(prepared.Authorization!);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
