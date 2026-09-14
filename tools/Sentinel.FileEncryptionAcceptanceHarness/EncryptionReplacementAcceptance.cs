using Sentinel.App.Services;
using System.Security.Cryptography;

internal static class EncryptionReplacementAcceptance
{
    internal static void Verify()
    {
        VerifySuccessfulReplacementRoundTrip();
        VerifyEncryptionFailurePreservesSource();
        VerifyUnverifiedResultPreservesSource();
        VerifyCancellationPreservesSource();
        VerifyCollisionPreservesSourceAndDestination();
        VerifySourceRetirementFailureIsNotReportedAsSuccess();
        VerifyPreReplacementContainerStillDecrypts();
    }

    private static void VerifySuccessfulReplacementRoundTrip()
    {
        string root = CreateRoot();
        char[] senderPassword = "replace plaintext after verify 2026!".ToCharArray();
        char[] recipientPassword = "replace plaintext after verify 2026!".ToCharArray();
        byte[] original = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 91);

        string source = Path.Combine(root, "replace-me.txt");
        string encrypted = source + ".sentinel.senc";
        string restored = Path.Combine(root, "restored.txt");
        File.WriteAllBytes(source, original);

        try
        {
            FileEncryptionService primitive = new(SentinelEncryptedContainerV1.MinimumChunkSize);
            FileEncryptionReplacementService replacement = new(primitive);

            using (PasswordFileKeyProtector sender = new(senderPassword))
            {
                FileEncryptionResult encryption = replacement.EncryptReplacingSourceAsync(
                    source,
                    encrypted,
                    new IFileKeyProtector[] { sender }).GetAwaiter().GetResult();

                Require(encryption.Succeeded && encryption.Verified,
                    "User-facing replacement encryption did not complete: " + encryption.Code + " / " + encryption.Message);
                Require(encryption.Code == "VerifiedAndSourceRetired",
                    "User-facing replacement encryption did not report verified source retirement.");
            }

            Require(!File.Exists(source),
                "User-facing encryption left the readable plaintext source beside the verified encrypted file.");
            Require(File.Exists(encrypted),
                "User-facing replacement encryption did not leave the verified .sentinel.senc file.");

            using (PasswordFileKeyProtector recipient = new(recipientPassword))
            {
                FileDecryptionResult decryption = primitive.DecryptAsync(
                    encrypted,
                    restored,
                    new IFileKeyProtector[] { recipient }).GetAwaiter().GetResult();
                Require(decryption.Succeeded,
                    "Replacement encrypted output could not be decrypted after source retirement: " + decryption.Code);
            }

            Require(File.Exists(restored), "Replacement round trip did not restore plaintext.");
            Require(File.ReadAllBytes(restored).AsSpan().SequenceEqual(original),
                "Replacement round trip did not restore the exact original bytes.");
        }
        finally
        {
            Array.Clear(senderPassword, 0, senderPassword.Length);
            Array.Clear(recipientPassword, 0, recipientPassword.Length);
            CryptographicOperations.ZeroMemory(original);
            Cleanup(root);
        }
    }

    private static void VerifyEncryptionFailurePreservesSource()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "encryption-failure.txt");
        string output = source + ".sentinel.senc";
        byte[] original = RandomNumberGenerator.GetBytes(173);
        File.WriteAllBytes(source, original);

        try
        {
            FileEncryptionReplacementService replacement = new((sourcePath, outputPath, _, _) =>
                Task.FromResult(FileEncryptionResult.Failure(
                    "InjectedEncryptionFailure",
                    "Acceptance injection: encryption failed before verification.",
                    sourcePath,
                    outputPath)));

            FileEncryptionResult result = replacement.EncryptReplacingSourceAsync(
                source,
                output,
                Array.Empty<IFileKeyProtector>()).GetAwaiter().GetResult();

            Require(!result.Succeeded && !result.Verified && result.Code == "InjectedEncryptionFailure",
                "Replacement transaction changed an encryption failure into success.");
            Require(File.Exists(source) && File.ReadAllBytes(source).AsSpan().SequenceEqual(original),
                "Encryption failure did not preserve the exact plaintext source.");
            Require(!File.Exists(output), "Injected pre-verification failure unexpectedly created an encrypted output.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            Cleanup(root);
        }
    }

    private static void VerifyUnverifiedResultPreservesSource()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "verification-failure.txt");
        string output = source + ".sentinel.senc";
        byte[] original = RandomNumberGenerator.GetBytes(211);
        File.WriteAllBytes(source, original);

        try
        {
            FileEncryptionReplacementService replacement = new((sourcePath, outputPath, _, _) =>
                Task.FromResult(new FileEncryptionResult(
                    Succeeded: true,
                    Verified: false,
                    Code: "InjectedUnverifiedResult",
                    Message: "Acceptance injection: final authenticated verification did not complete.",
                    SourcePath: sourcePath,
                    OutputPath: outputPath,
                    PlaintextBytes: original.Length,
                    InvalidOutputRemains: false)));

            FileEncryptionResult result = replacement.EncryptReplacingSourceAsync(
                source,
                output,
                Array.Empty<IFileKeyProtector>()).GetAwaiter().GetResult();

            Require(!result.Succeeded && !result.Verified && result.Code == "VerificationNotCompleted",
                "Replacement transaction did not fail closed when authenticated verification was incomplete.");
            Require(File.Exists(source) && File.ReadAllBytes(source).AsSpan().SequenceEqual(original),
                "An unverified encrypted result retired or modified the plaintext source.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            Cleanup(root);
        }
    }

    private static void VerifyCancellationPreservesSource()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "cancel-me.txt");
        string output = source + ".sentinel.senc";
        byte[] original = RandomNumberGenerator.GetBytes(8192);
        File.WriteAllBytes(source, original);

        try
        {
            FileEncryptionReplacementService replacement = new((_, _, _, _) =>
                Task.FromException<FileEncryptionResult>(new OperationCanceledException("Acceptance injection: canceled before verified completion.")));

            FileEncryptionResult result = replacement.EncryptReplacingSourceAsync(
                source,
                output,
                Array.Empty<IFileKeyProtector>()).GetAwaiter().GetResult();

            Require(!result.Succeeded && !result.Verified && result.Code == "Canceled",
                "Canceled replacement encryption did not fail closed as Canceled.");
            Require(File.Exists(source) && File.ReadAllBytes(source).AsSpan().SequenceEqual(original),
                "Canceled replacement encryption did not preserve the exact plaintext source.");
            Require(!File.Exists(output),
                "Injected cancellation unexpectedly created an encrypted output.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            Cleanup(root);
        }
    }

    private static void VerifyCollisionPreservesSourceAndDestination()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "collision-source.txt");
        string output = source + ".sentinel.senc";
        byte[] original = RandomNumberGenerator.GetBytes(257);
        byte[] existingOutput = RandomNumberGenerator.GetBytes(97);
        File.WriteAllBytes(source, original);
        File.WriteAllBytes(output, existingOutput);

        try
        {
            FileEncryptionService primitive = new(SentinelEncryptedContainerV1.MinimumChunkSize);
            FileEncryptionReplacementService replacement = new(primitive);
            TestKeyProtector protector = new(3, RandomNumberGenerator.GetBytes(32));

            FileEncryptionResult result = replacement.EncryptReplacingSourceAsync(
                source,
                output,
                new IFileKeyProtector[] { protector }).GetAwaiter().GetResult();

            Require(!result.Succeeded && result.Code == "OutputCollision",
                "Replacement encryption did not reject an existing encrypted destination.");
            Require(File.Exists(source) && File.ReadAllBytes(source).AsSpan().SequenceEqual(original),
                "Destination collision modified or retired the plaintext source.");
            Require(File.ReadAllBytes(output).AsSpan().SequenceEqual(existingOutput),
                "Destination collision overwrote existing user data.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            CryptographicOperations.ZeroMemory(existingOutput);
            Cleanup(root);
        }
    }

    private static void VerifySourceRetirementFailureIsNotReportedAsSuccess()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "retirement-blocked.txt");
        string output = source + ".sentinel.senc";
        byte[] original = RandomNumberGenerator.GetBytes(4096);
        File.WriteAllBytes(source, original);

        try
        {
            FileEncryptionService primitive = new(SentinelEncryptedContainerV1.MinimumChunkSize);
            FileEncryptionReplacementService replacement = new(primitive);
            TestKeyProtector protector = new(3, RandomNumberGenerator.GetBytes(32));

            // On Windows, this handle permits readers (including Sentinel's encryptor) but denies delete sharing.
            // The container can therefore be completed and verified while exact source retirement is forced to fail.
            using FileStream deleteBlocker = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            FileEncryptionResult result = replacement.EncryptReplacingSourceAsync(
                source,
                output,
                new IFileKeyProtector[] { protector }).GetAwaiter().GetResult();

            Require(!result.Succeeded && result.Verified && result.Code == "SourceRetirementFailed",
                "A verified container with blocked plaintext retirement was incorrectly reported as full success: " + result.Code);
            Require(File.Exists(source) && File.ReadAllBytes(source).AsSpan().SequenceEqual(original),
                "Failed source retirement damaged the original plaintext.");
            Require(File.Exists(output),
                "Source-retirement failure destroyed the already verified encrypted copy.");
            Require(primitive.VerifyAsync(output, new IFileKeyProtector[] { protector }).GetAwaiter().GetResult().Succeeded,
                "Encrypted copy was not usable after a source-retirement failure.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            Cleanup(root);
        }
    }

    private static void VerifyPreReplacementContainerStillDecrypts()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "pre-replacement-source.bin");
        string encrypted = Path.Combine(root, "pre-replacement-container.sentinel.senc");
        string restored = Path.Combine(root, "pre-replacement-restored.bin");
        byte[] original = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 33);
        File.WriteAllBytes(source, original);

        try
        {
            // FileEncryptionService is the pre-replacement, non-destructive container writer that existing
            // Sentinel v1 .senc files used. Replacement UX must not alter the reader/container format.
            FileEncryptionService primitive = new(SentinelEncryptedContainerV1.MinimumChunkSize);
            TestKeyProtector protector = new(3, RandomNumberGenerator.GetBytes(32));
            FileEncryptionResult legacyStyle = primitive.EncryptAsync(
                source,
                encrypted,
                new IFileKeyProtector[] { protector }).GetAwaiter().GetResult();
            Require(legacyStyle.Succeeded && legacyStyle.Verified && File.Exists(source),
                "Could not create the pre-replacement-style v1 compatibility container.");

            FileDecryptionResult restoredResult = primitive.DecryptAsync(
                encrypted,
                restored,
                new IFileKeyProtector[] { protector }).GetAwaiter().GetResult();
            Require(restoredResult.Succeeded,
                "A pre-replacement-style .sentinel.senc container no longer decrypts: " + restoredResult.Code);
            Require(File.ReadAllBytes(restored).AsSpan().SequenceEqual(original),
                "Pre-replacement-style compatibility decryption changed plaintext bytes.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            Cleanup(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelEncryptionReplacementAcceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
