using Sentinel.App.Services;
using System.Buffers.Binary;
using System.Security.Cryptography;

internal static class PasswordProtectionAcceptance
{
    internal static void Verify()
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
