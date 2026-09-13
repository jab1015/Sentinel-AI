using Sentinel.App.Services;
using System.Reflection;
using System.Security.Cryptography;

internal static class SecureDeleteRecoveryAcceptance
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteRecoveryHarness", Guid.NewGuid().ToString("N"));
        string journalRoot = Path.Combine(root, "journal");
        Directory.CreateDirectory(root);
        try
        {
            VerifyPreparedExactTarget(root, journalRoot);
            VerifyPreparedReplacement(root, journalRoot);
            VerifyMutationStartedAmbiguity(root, journalRoot);
            VerifyRemovalContradiction(root, journalRoot);
            VerifyRemovalPathReuse(root, journalRoot);
            VerifyRecoveryRequired(root, journalRoot);
            VerifyNoMutationSurface();

            Console.WriteLine("Secure Delete recovery pre-mutation exact-object classification: PASS");
            Console.WriteLine("Secure Delete recovery path-reuse / contradiction detection: PASS");
            Console.WriteLine("Secure Delete mutation-started ambiguity remains fail-closed: PASS");
            Console.WriteLine("Secure Delete recovery classifier remains read-only: PASS");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void VerifyPreparedExactTarget(string root, string journalRoot)
    {
        string path = Path.Combine(root, "prepared-intact.bin");
        byte[] original = "prepared exact target"u8.ToArray();
        File.WriteAllBytes(path, original);
        string hash = Convert.ToHexString(SHA256.HashData(original));

        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "prepared-intact"));
        SecureDeleteOperationRecord record = Begin(journal, path);
        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);

        Require(assessment.Phase == SecureDeleteRecoveryPhase.PreMutation, "Prepared journal did not classify as pre-mutation.");
        Require(assessment.TargetObservation == SecureDeleteRecoveryTargetObservation.ExactOriginalPresent,
            "Prepared exact target was not recognized as the original object.");
        Require(!assessment.RequiresUserAttention && assessment.MayRequestFreshPreMutationAuthorization,
            "Prepared intact target did not allow only fresh future authorization.");
        Require(!assessment.MayAutomaticallyResumeMutation, "Recovery classifier granted automatic mutation authority.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash,
            "Recovery classification changed prepared target content.");
    }

    private static void VerifyPreparedReplacement(string root, string journalRoot)
    {
        string path = Path.Combine(root, "prepared-swap.bin");
        string moved = Path.Combine(root, "prepared-swap-original.bin");
        File.WriteAllText(path, "approved original");
        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "prepared-swap"));
        SecureDeleteOperationRecord record = Begin(journal, path);

        File.Move(path, moved);
        File.WriteAllText(path, "replacement object");
        string replacement = File.ReadAllText(path);
        string original = File.ReadAllText(moved);

        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(assessment.TargetObservation == SecureDeleteRecoveryTargetObservation.DifferentObjectPresent,
            "Path replacement was not classified as a different object.");
        Require(assessment.RequiresUserAttention && !assessment.MayRequestFreshPreMutationAuthorization &&
                !assessment.MayAutomaticallyResumeMutation,
            "Path replacement retained recovery authority.");
        Require(File.ReadAllText(path) == replacement && File.ReadAllText(moved) == original,
            "Recovery classification modified a replacement or original object.");
    }

    private static void VerifyMutationStartedAmbiguity(string root, string journalRoot)
    {
        string path = Path.Combine(root, "mutation-started.bin");
        File.WriteAllText(path, "mutation started but file still exists");
        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "mutation-started"));
        SecureDeleteOperationRecord record = Begin(journal, path);
        record = journal.Advance(record, SecureDeleteOperationState.IdentityVerified);
        record = journal.Advance(record, SecureDeleteOperationState.PrimaryMutationStarted);

        SecureDeleteRecoveryAssessment present = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(present.Phase == SecureDeleteRecoveryPhase.MutationStarted &&
                present.TargetObservation == SecureDeleteRecoveryTargetObservation.ExactOriginalPresent &&
                present.RequiresUserAttention && !present.MayAutomaticallyResumeMutation,
            "Mutation-started exact target did not remain an ambiguous manual-recovery state.");

        string moved = Path.Combine(root, "mutation-started-moved.bin");
        File.Move(path, moved);
        SecureDeleteRecoveryAssessment missing = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(missing.Phase == SecureDeleteRecoveryPhase.MutationStarted &&
                missing.TargetObservation == SecureDeleteRecoveryTargetObservation.MissingOrInaccessible &&
                missing.RequiresUserAttention && !missing.MayAutomaticallyResumeMutation,
            "Mutation-started missing target was treated as safe automatic continuation.");
        Require(File.Exists(moved), "Recovery classification removed a moved mutation-started target.");
    }

    private static void VerifyRemovalContradiction(string root, string journalRoot)
    {
        string path = Path.Combine(root, "removal-contradiction.bin");
        File.WriteAllText(path, "journal says removed but exact target exists");
        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "removal-contradiction"));
        SecureDeleteOperationRecord record = Begin(journal, path);
        record = journal.Advance(record, SecureDeleteOperationState.IdentityVerified);
        record = journal.Advance(record, SecureDeleteOperationState.PrimaryMutationStarted);
        record = journal.Advance(record, SecureDeleteOperationState.PrimaryRemovalVerified);

        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(assessment.Phase == SecureDeleteRecoveryPhase.RemovalRecorded &&
                assessment.TargetObservation == SecureDeleteRecoveryTargetObservation.ExactOriginalPresent &&
                assessment.RequiresUserAttention && !assessment.MayAutomaticallyResumeMutation,
            "Recovery trusted a removal record that contradicted live exact-object evidence.");
        Require(File.Exists(path), "Contradiction classification mutated the exact original target.");
    }

    private static void VerifyRemovalPathReuse(string root, string journalRoot)
    {
        string path = Path.Combine(root, "removal-reuse.bin");
        string moved = Path.Combine(root, "removal-reuse-original.bin");
        File.WriteAllText(path, "original object");
        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "removal-reuse"));
        SecureDeleteOperationRecord record = Begin(journal, path);
        record = journal.Advance(record, SecureDeleteOperationState.IdentityVerified);
        record = journal.Advance(record, SecureDeleteOperationState.PrimaryMutationStarted);
        record = journal.Advance(record, SecureDeleteOperationState.PrimaryRemovalVerified);

        File.Move(path, moved);
        File.WriteAllText(path, "unrelated replacement");
        string replacement = File.ReadAllText(path);

        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(assessment.TargetObservation == SecureDeleteRecoveryTargetObservation.DifferentObjectPresent &&
                assessment.RequiresUserAttention && !assessment.MayAutomaticallyResumeMutation,
            "Post-removal path reuse was not isolated from prior Secure Delete authority.");
        Require(File.ReadAllText(path) == replacement && File.Exists(moved),
            "Recovery classification touched a post-removal replacement or moved original.");
    }

    private static void VerifyRecoveryRequired(string root, string journalRoot)
    {
        string path = Path.Combine(root, "recovery-required.bin");
        File.WriteAllText(path, "explicit recovery required");
        SecureDeleteOperationJournal journal = new(Path.Combine(journalRoot, "recovery-required"));
        SecureDeleteOperationRecord record = Begin(journal, path);
        record = journal.Advance(record, SecureDeleteOperationState.RecoveryRequired, "simulated crash ambiguity");

        SecureDeleteRecoveryAssessment assessment = new SecureDeleteRecoveryClassifier(journal).Assess(record.OperationId);
        Require(assessment.Phase == SecureDeleteRecoveryPhase.RecoveryRequired && assessment.RequiresUserAttention &&
                !assessment.MayRequestFreshPreMutationAuthorization && !assessment.MayAutomaticallyResumeMutation,
            "RecoveryRequired did not remain fail-closed.");
        Require(File.Exists(path), "RecoveryRequired classification changed the target.");
    }

    private static SecureDeleteOperationRecord Begin(SecureDeleteOperationJournal journal, string path)
    {
        SecureDeleteTargetValidationResult validated = SecureDeleteTargetValidator.Validate(path);
        Require(validated.Succeeded, "Recovery fixture failed validation: " + validated.Code);
        SecureDeletePreparationResult prepared = new SecureDeleteCoordinator().Prepare(validated.Target);
        Require(prepared.Succeeded && prepared.Authorization is not null,
            "Recovery fixture failed authorization: " + prepared.Code);
        return journal.Begin(prepared.Authorization!);
    }

    private static void VerifyNoMutationSurface()
    {
        MethodInfo[] methods = typeof(SecureDeleteRecoveryClassifier)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Require(!methods.Any(method => method.ReturnType == typeof(void) && method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)),
            "Recovery classifier exposed a delete-like command surface.");
        Require(!methods.SelectMany(method => method.GetParameters()).Any(parameter => parameter.ParameterType == typeof(string)),
            "Recovery classifier exposed a free-form path parameter.");

        string sourcePath = Path.Combine(Environment.CurrentDirectory,
            "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "SecureDeleteRecoveryClassifier.cs");
        Require(File.Exists(sourcePath), "Recovery classifier source was unavailable to the acceptance harness.");
        string source = File.ReadAllText(sourcePath);
        Require(!source.Contains("File.Move(", StringComparison.Ordinal) &&
                !source.Contains("File.Write", StringComparison.Ordinal) &&
                !source.Contains("SetFileInformationByHandle", StringComparison.Ordinal),
            "Recovery classifier unexpectedly contains filesystem mutation code.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
