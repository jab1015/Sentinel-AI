using Sentinel.App.Services;

internal static class SecureDeleteCoordinatorAcceptance
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteCoordinatorHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DateTimeOffset now = new(2026, 9, 12, 23, 55, 0, TimeSpan.Zero);
            SecureDeleteCoordinator coordinator = new(() => now);

            SecureDeletePreparationResult empty = coordinator.Prepare(default);
            Require(!empty.Succeeded && empty.Code == SecureDeleteCoordinatorCode.InvalidTarget,
                "Coordinator accepted an unbound/default target identity.");

            string targetPath = Path.Combine(root, "approved.bin");
            File.WriteAllText(targetPath, "approved exact object");
            SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Validate(targetPath);
            Require(validation.Succeeded, "Coordinator fixture failed exact-target validation: " + validation.Code);

            SecureDeletePreparationResult prepared = coordinator.Prepare(validation.Target);
            Require(prepared.Succeeded && prepared.Code == SecureDeleteCoordinatorCode.Prepared,
                "Validated local target was not prepared: " + prepared.Code);
            Require(prepared.Authorization is not null, "Prepared result did not contain an authorization.");
            Require(prepared.Storage is not null && prepared.Storage.LocationKind == StorageLocationKind.LocalFixed,
                "Prepared authorization was not bound to local fixed storage.");

            SecureDeleteAuthorization authorization = prepared.Authorization!;
            Require(authorization.Target == validation.Target,
                "Authorization did not retain the exact approved filesystem identity.");
            Require(authorization.AllowsLogicalRemoval,
                "Coordinator failed to express the narrow future logical-removal privilege.");
            Require(!authorization.AllowsOverwriteSanitization,
                "Coordinator authorized overwrite sanitization before media/strategy qualification.");
            Require(authorization.ExpiresUtc - authorization.CreatedUtc == SecureDeleteCoordinator.AuthorizationLifetime,
                "Secure Delete authorization lifetime changed unexpectedly.");

            SecureDeleteMutationGateResult ready = coordinator.RevalidateForMutation(authorization);
            Require(ready.Succeeded && ready.Code == SecureDeleteCoordinatorCode.MutationGateReady,
                "Unchanged exact target did not pass immediate pre-mutation revalidation: " + ready.Code);
            Require(File.Exists(targetPath), "Non-destructive coordinator gate modified the approved file.");

            SecureDeleteAuthorization escalated = authorization with { AllowsOverwriteSanitization = true };
            SecureDeleteMutationGateResult escalatedResult = coordinator.RevalidateForMutation(escalated);
            Require(!escalatedResult.Succeeded && escalatedResult.Code == SecureDeleteCoordinatorCode.InvalidAuthorization,
                "Authorization privilege inflation was accepted.");
            Require(File.Exists(targetPath), "Privilege-inflation rejection modified the target.");

            now = authorization.ExpiresUtc + TimeSpan.FromMilliseconds(1);
            SecureDeleteMutationGateResult expired = coordinator.RevalidateForMutation(authorization);
            Require(!expired.Succeeded && expired.Code == SecureDeleteCoordinatorCode.AuthorizationExpired,
                "Expired Secure Delete authorization remained usable.");
            Require(File.Exists(targetPath), "Expired-authorization rejection modified the target.");

            now = authorization.CreatedUtc + TimeSpan.FromSeconds(30);
            string movedOriginal = Path.Combine(root, "approved-original.bin");
            File.Move(targetPath, movedOriginal);
            File.WriteAllText(targetPath, "replacement object");
            SecureDeleteMutationGateResult swapped = coordinator.RevalidateForMutation(authorization);
            Require(!swapped.Succeeded && swapped.Code == SecureDeleteCoordinatorCode.IdentityChanged,
                "Path replacement retained prior Secure Delete authorization.");
            Require(File.Exists(targetPath) && File.Exists(movedOriginal),
                "Path-swap rejection modified either filesystem object.");

            SecureDeleteAuthorization malformed = authorization with
            {
                AuthorizationId = Guid.Empty,
                Target = default
            };
            SecureDeleteMutationGateResult malformedResult = coordinator.RevalidateForMutation(malformed);
            Require(!malformedResult.Succeeded && malformedResult.Code == SecureDeleteCoordinatorCode.InvalidAuthorization,
                "Malformed Secure Delete authorization was accepted.");

            Console.WriteLine("Secure Delete short-lived exact-identity authorization: PASS");
            Console.WriteLine("Secure Delete pre-mutation path-swap revocation: PASS");
            Console.WriteLine("Secure Delete privilege-inflation / expiry rejection: PASS");
            Console.WriteLine("Secure Delete coordinator remains non-destructive: PASS");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
