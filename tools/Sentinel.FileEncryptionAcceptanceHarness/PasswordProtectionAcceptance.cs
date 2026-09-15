using Sentinel.App.Services;
using System.Buffers.Binary;
using System.Security.Cryptography;

internal static class PasswordProtectionAcceptance
{
    internal static void Verify()
    {
        VerifyProtectorRecordSafety();
        VerifyPortableContainerRoundTrip();
    }

    private static void VerifyProtectorRecordSafety()
    {
        char[] password = "correct horse battery staple ✓".ToCharArray();
        char[] wrongPassword = "definitely wrong password".ToCharArray();
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            using PasswordFileKeyProtector protector = new(password);
            using PasswordFileKeyProtector wrong = new(wrongPassword);

            WrappedFileKeyRecord first = protector.WrapAsync(dek, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            WrappedFileKeyRecord second = protector.WrapAsync(dek, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            Require(first.RecordVersion == 1 && first.ProtectionModeId == PasswordFileKeyProtector.ModeId,
                "Password key record version/mode changed unexpectedly.");
            Require(first.WrappingAlgorithmId == PasswordFileKeyProtector.WrappingAlgorithmArgon2idAesGcm,
                "Password key record did not use Argon2id + authenticated AES-GCM wrapping.");
            Require(first.WrappedDek.Length == 48, "Password protector did not emit the expected 32-byte ciphertext + 16-byte tag.");
            Require(first.Parameters.Length == 41, "Password protector parameter layout changed without a format version change.");
            Require(first.Parameters[0] == 1, "Password protector parameter version changed unexpectedly.");

            int memoryKiB = BinaryPrimitives.ReadInt32LittleEndian(first.Parameters.AsSpan(17, 4));
            int iterations = BinaryPrimitives.ReadInt32LittleEndian(first.Parameters.AsSpan(21, 4));
            int parallelism = BinaryPrimitives.ReadInt32LittleEndian(first.Parameters.AsSpan(25, 4));
            Require(memoryKiB == PasswordFileKeyProtector.DefaultMemoryKiB,
                "Password key record did not persist the expected 64 MiB Argon2id memory cost.");
            Require(iterations == PasswordFileKeyProtector.DefaultIterations,
                "Password key record did not persist the expected Argon2id iteration count.");
            Require(parallelism == PasswordFileKeyProtector.DefaultParallelism,
                "Password key record did not persist the expected Argon2id lane count.");

            Require(!first.Parameters.AsSpan(1, 16).SequenceEqual(second.Parameters.AsSpan(1, 16)),
                "Two password key records reused the Argon2id salt.");
            Require(!first.Parameters.AsSpan(29, 12).SequenceEqual(second.Parameters.AsSpan(29, 12)),
                "Two password key records reused the AES-GCM wrapping nonce.");

            byte[]? unwrapped = protector.TryUnwrapAsync(first, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Require(unwrapped is not null && unwrapped.AsSpan().SequenceEqual(dek),
                "Correct password did not recover the exact file DEK.");
            if (unwrapped is not null) CryptographicOperations.ZeroMemory(unwrapped);

            byte[]? wrongResult = wrong.TryUnwrapAsync(first, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Require(wrongResult is null, "Wrong password recovered the file DEK.");
            if (wrongResult is not null) CryptographicOperations.ZeroMemory(wrongResult);

            byte[] excessiveMemory = first.Parameters.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(excessiveMemory.AsSpan(17, 4), 1024 * 1024);
            WrappedFileKeyRecord excessiveMemoryRecord = first with { Parameters = excessiveMemory };
            Require(protector.TryUnwrapAsync(excessiveMemoryRecord, CancellationToken.None).AsTask().GetAwaiter().GetResult() is null,
                "Untrusted password record could request an excessive Argon2 memory allocation.");

            byte[] weakMemory = first.Parameters.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(weakMemory.AsSpan(17, 4), 8 * 1024);
            WrappedFileKeyRecord weakMemoryRecord = first with { Parameters = weakMemory };
            Require(protector.TryUnwrapAsync(weakMemoryRecord, CancellationToken.None).AsTask().GetAwaiter().GetResult() is null,
                "Untrusted password record could downgrade Argon2 memory below Sentinel's v1 floor.");

            byte[] tamperedWrapped = first.WrappedDek.ToArray();
            tamperedWrapped[0] ^= 0x01;
            Require(protector.TryUnwrapAsync(first with { WrappedDek = tamperedWrapped }, CancellationToken.None).AsTask().GetAwaiter().GetResult() is null,
                "Password protector accepted a tampered wrapped DEK.");

            protector.Dispose();
            bool disposedRejected = false;
            try { _ = protector.WrapAsync(dek, CancellationToken.None).AsTask().GetAwaiter().GetResult(); }
            catch (ObjectDisposedException) { disposedRejected = true; }
            Require(disposedRejected, "Disposed password protector remained usable with retained password material.");
        }
        finally
        {
            Array.Clear(password, 0, password.Length);
            Array.Clear(wrongPassword, 0, wrongPassword.Length);
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static void VerifyPortableContainerRoundTrip()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelPortableEncryptionAcceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        char[] senderPassword = "portable sharing password 2026!".ToCharArray();
        char[] recipientPassword = "portable sharing password 2026!".ToCharArray();
        char[] wrongPassword = "portable sharing password WRONG".ToCharArray();
        try
        {
            string source = Path.Combine(root, "share-me.txt");
            string encrypted = source + ".sentinel.senc";
            string restored = Path.Combine(root, "received-share-me.txt");
            string wrongOutput = Path.Combine(root, "wrong-password-output.txt");
            byte[] original = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 137);
            File.WriteAllBytes(source, original);
            byte[] originalHash = SHA256.HashData(original);

            FileEncryptionService service = new(SentinelEncryptedContainerV1.MinimumChunkSize);
            using (PasswordFileKeyProtector sender = new(senderPassword))
            {
                FileEncryptionResult encryption = service.EncryptAsync(
                    source,
                    encrypted,
                    new IFileKeyProtector[] { sender }).GetAwaiter().GetResult();

                Require(encryption.Succeeded && encryption.Verified,
                    "Portable password encryption was not verified: " + encryption.Code);
            }

            Require(File.Exists(source), "Portable encryption removed the original plaintext source.");
            Require(SHA256.HashData(File.ReadAllBytes(source)).AsSpan().SequenceEqual(originalHash),
                "Portable encryption modified the original plaintext source.");
            Require(File.Exists(encrypted), "Portable encryption did not create the .sentinel.senc output.");

            // A fresh protector instance represents a recipient-side Sentinel process. No Windows-bound
            // protector or sender-side key object is reused here; the password alone must be sufficient.
            using (PasswordFileKeyProtector recipient = new(recipientPassword))
            {
                FileContainerVerificationResult verification = service.VerifyAsync(
                    encrypted,
                    new IFileKeyProtector[] { recipient }).GetAwaiter().GetResult();
                Require(verification.Succeeded, "Recipient password could not authenticate the portable container: " + verification.Code);

                FileDecryptionResult decryption = service.DecryptAsync(
                    encrypted,
                    restored,
                    new IFileKeyProtector[] { recipient }).GetAwaiter().GetResult();
                Require(decryption.Succeeded, "Recipient password could not decrypt the portable container: " + decryption.Code);
            }

            Require(File.Exists(restored), "Portable decryption did not create the separate restored plaintext file.");
            Require(File.ReadAllBytes(restored).AsSpan().SequenceEqual(original),
                "Portable recipient decryption did not restore the exact original bytes.");
            Require(File.Exists(encrypted), "Portable decryption removed or changed ownership of the encrypted container.");

            using (PasswordFileKeyProtector wrong = new(wrongPassword))
            {
                FileDecryptionResult rejected = service.DecryptAsync(
                    encrypted,
                    wrongOutput,
                    new IFileKeyProtector[] { wrong }).GetAwaiter().GetResult();
                Require(!rejected.Succeeded && rejected.Code == "KeyUnavailable",
                    "Wrong sharing password was not rejected as KeyUnavailable.");
                Require(!rejected.InvalidOutputRemains,
                    "Wrong sharing password left an invalid plaintext output requiring cleanup.");
            }
            Require(!File.Exists(wrongOutput), "Wrong sharing password left plaintext output on disk.");

            string collisionOutput = Path.Combine(root, "existing-output.txt");
            File.WriteAllText(collisionOutput, "must-not-be-overwritten");
            using (PasswordFileKeyProtector recipient = new(recipientPassword))
            {
                FileDecryptionResult collision = service.DecryptAsync(
                    encrypted,
                    collisionOutput,
                    new IFileKeyProtector[] { recipient }).GetAwaiter().GetResult();
                Require(!collision.Succeeded && collision.Code == "OutputCollision",
                    "Portable decryption did not refuse an existing plaintext destination.");
            }
            Require(File.ReadAllText(collisionOutput) == "must-not-be-overwritten",
                "Portable decryption modified an existing destination despite collision refusal.");

            CryptographicOperations.ZeroMemory(original);
            CryptographicOperations.ZeroMemory(originalHash);
        }
        finally
        {
            Array.Clear(senderPassword, 0, senderPassword.Length);
            Array.Clear(recipientPassword, 0, recipientPassword.Length);
            Array.Clear(wrongPassword, 0, wrongPassword.Length);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}