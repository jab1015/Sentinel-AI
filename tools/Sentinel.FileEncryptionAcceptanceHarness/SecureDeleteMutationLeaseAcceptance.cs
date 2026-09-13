using Microsoft.Win32.SafeHandles;
using Sentinel.App.Services;
using System.Reflection;

internal static class SecureDeleteMutationLeaseAcceptance
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteMutationLeaseHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string target = Path.Combine(root, "lease-target.bin");
            byte[] original = "retained exact-object lease fixture"u8.ToArray();
            File.WriteAllBytes(target, original);
            Require(File.ReadAllBytes(target).SequenceEqual(original), "Lease fixture bytes were not established before acquisition.");

            SecureDeleteTargetValidationResult validated = SecureDeleteTargetValidator.Validate(target);
            Require(validated.Succeeded, "Lease fixture did not validate: " + validated.Code);

            SecureDeleteCoordinator coordinator = new();
            SecureDeletePreparationResult prepared = coordinator.Prepare(validated.Target);
            Require(prepared.Succeeded && prepared.Authorization is not null,
                "Lease fixture did not receive Secure Delete authorization: " + prepared.Code);

            SecureDeleteMutationLeaseManager manager = new(coordinator);
            SecureDeleteMutationLeaseResult acquired = manager.Acquire(prepared.Authorization);
            Require(acquired.Succeeded && acquired.Code == SecureDeleteMutationLeaseCode.Acquired && acquired.Lease is not null,
                "Exact mutation lease was not acquired: " + acquired.Code);

            using (SecureDeleteMutationLease lease = acquired.Lease!)
            {
                Require(lease.IsActive, "Returned mutation lease did not retain a live exact-object handle.");
                Require(lease.AuthorizationId == prepared.Authorization!.AuthorizationId,
                    "Mutation lease was not bound to the approved authorization.");
                Require(lease.Target == validated.Target,
                    "Mutation lease changed the approved stable target identity.");

                string movedWhileLeased = Path.Combine(root, "should-not-move.bin");
                bool renameBlocked = false;
                try { File.Move(target, movedWhileLeased); }
                catch (IOException) { renameBlocked = true; }
                catch (UnauthorizedAccessException) { renameBlocked = true; }
                Require(renameBlocked && !File.Exists(movedWhileLeased),
                    "Live mutation lease did not block a rename/replacement race.");

                bool writeBlocked = false;
                try
                {
                    using FileStream stream = new(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (IOException) { writeBlocked = true; }
                catch (UnauthorizedAccessException) { writeBlocked = true; }
                Require(writeBlocked,
                    "Live mutation lease did not block a concurrent write-capable open.");
            }

            Require(File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(original),
                "Acquiring and disposing the non-destructive mutation lease changed the file.");
            string movedAfterDispose = Path.Combine(root, "moved-after-dispose.bin");
            File.Move(target, movedAfterDispose);
            Require(File.Exists(movedAfterDispose), "Disposing the mutation lease did not release rename protection.");
            File.Move(movedAfterDispose, target);

            string swap = Path.Combine(root, "swap.bin");
            string originalMoved = Path.Combine(root, "swap-original.bin");
            File.WriteAllText(swap, "approved exact object");
            SecureDeleteTargetValidationResult swapValidated = SecureDeleteTargetValidator.Validate(swap);
            Require(swapValidated.Succeeded, "Lease swap fixture was not initially valid.");
            SecureDeletePreparationResult swapPrepared = coordinator.Prepare(swapValidated.Target);
            Require(swapPrepared.Succeeded && swapPrepared.Authorization is not null,
                "Lease swap fixture was not authorized.");
            Require(coordinator.RevalidateForMutation(swapPrepared.Authorization).Succeeded,
                "Lease swap fixture did not pass preflight before the deliberate race.");

            File.Move(swap, originalMoved);
            File.WriteAllText(swap, "replacement object");
            SecureDeleteMutationLeaseResult swapped = manager.Acquire(swapPrepared.Authorization);
            Require(!swapped.Succeeded && swapped.Code == SecureDeleteMutationLeaseCode.MutationGateRejected,
                "Path replacement between preflight and lease acquisition retained authority.");
            Require(swapped.CoordinatorCode == SecureDeleteCoordinatorCode.IdentityChanged,
                "Path replacement was not classified as exact-identity change.");
            Require(File.Exists(swap) && File.Exists(originalMoved),
                "Rejected path swap modified either filesystem object.");

            SecureDeleteAuthorization inflated = prepared.Authorization! with { AllowsOverwriteSanitization = true };
            SecureDeleteMutationLeaseResult inflatedResult = manager.Acquire(inflated);
            Require(!inflatedResult.Succeeded &&
                    inflatedResult.Code == SecureDeleteMutationLeaseCode.MutationGateRejected &&
                    inflatedResult.CoordinatorCode == SecureDeleteCoordinatorCode.InvalidAuthorization,
                "Privilege-inflated authorization acquired a mutation lease.");

            VerifyNoPathOrMutationSurface();

            Console.WriteLine("Secure Delete retained exact-handle mutation lease: PASS");
            Console.WriteLine("Secure Delete lease blocks rename/write races while active: PASS");
            Console.WriteLine("Secure Delete preflight-to-lease path-swap revocation: PASS");
            Console.WriteLine("Secure Delete mutation lease remains non-destructive: PASS");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void VerifyNoPathOrMutationSurface()
    {
        MethodInfo[] managerMethods = typeof(SecureDeleteMutationLeaseManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Require(!managerMethods.SelectMany(method => method.GetParameters()).Any(parameter => parameter.ParameterType == typeof(string)),
            "Mutation lease manager exposed a string-path authority surface.");

        Require(!typeof(SecureDeleteMutationLease)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(property => property.PropertyType == typeof(SafeFileHandle)),
            "Mutation lease exposed its raw SafeFileHandle as a property.");

        string sourcePath = Path.Combine(Environment.CurrentDirectory,
            "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "SecureDeleteMutationLease.cs");
        Require(File.Exists(sourcePath), "Mutation lease source was unavailable to the acceptance harness.");
        string source = File.ReadAllText(sourcePath);
        string[] forbiddenMutationCalls =
        {
            "SetFileInformationByHandle",
            "DeleteFileW(",
            "File.Delete(",
            "WriteFile(",
            "SetEndOfFile",
            "FileDispositionInfo"
        };
        foreach (string forbidden in forbiddenMutationCalls)
        {
            Require(source.IndexOf(forbidden, StringComparison.Ordinal) < 0,
                "Mutation lease foundation unexpectedly contains destructive API surface: " + forbidden);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
