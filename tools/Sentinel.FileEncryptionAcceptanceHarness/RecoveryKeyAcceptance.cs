using Sentinel.App.Services;
using System.Security.Cryptography;

internal static class RecoveryKeyAcceptance
{
    internal static void Verify()
    {
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            using RecoveryKeyMaterial generated = RecoveryKeyMaterial.Generate();
            string display = generated.ToDisplayString();
            Require(display.StartsWith("SAI-RK1-", StringComparison.Ordinal),
                "Recovery key display format lost its versioned Sentinel prefix.");
            Require(!display.Contains(' '), "Recovery key display format unexpectedly contains whitespace.");

            Require(RecoveryKeyMaterial.TryParse(display, out RecoveryKeyMaterial? parsed) && parsed is not null,
                "A generated recovery key could not be parsed back.");
            using (parsed!)
            using (RecoveryKeyFileKeyProtector generatedProtector = generated.CreateProtector())
            using (RecoveryKeyFileKeyProtector parsedProtector = parsed.CreateProtector())
            {
                WrappedFileKeyRecord first = generatedProtector.WrapAsync(dek, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                WrappedFileKeyRecord second = generatedProtector.WrapAsync(dek, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                Require(first.RecordVersion == 1 && first.ProtectionModeId == RecoveryKeyFileKeyProtector.ModeId,
                    "Recovery key record version/mode changed unexpectedly.");
                Require(first.WrappingAlgorithmId == RecoveryKeyFileKeyProtector.WrappingAlgorithmAesGcm,
                    "Recovery key protector did not use authenticated AES-GCM wrapping.");
                Require(first.Parameters.Length == 13 && first.Parameters[0] == 1,
                    "Recovery key parameter layout changed without a version change.");
                Require(first.WrappedDek.Length == 48,
                    "Recovery key protector did not emit the expected ciphertext + tag length.");
                Require(!first.Parameters.AsSpan(1, 12).SequenceEqual(second.Parameters.AsSpan(1, 12)),
                    "Two recovery-key wraps reused the AES-GCM nonce.");

                byte[]? unwrapped = parsedProtector.TryUnwrapAsync(first, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                Require(unwrapped is not null && unwrapped.AsSpan().SequenceEqual(dek),
                    "Parsed recovery key did not recover the exact DEK.");
                if (unwrapped is not null) CryptographicOperations.ZeroMemory(unwrapped);

                using RecoveryKeyMaterial wrongMaterial = RecoveryKeyMaterial.Generate();
                using RecoveryKeyFileKeyProtector wrongProtector = wrongMaterial.CreateProtector();
                byte[]? wrong = wrongProtector.TryUnwrapAsync(first, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                Require(wrong is null, "An unrelated recovery key recovered the DEK.");
                if (wrong is not null) CryptographicOperations.ZeroMemory(wrong);

                byte[] tampered = first.WrappedDek.ToArray();
                tampered[0] ^= 0x01;
                byte[]? tamperedResult = parsedProtector.TryUnwrapAsync(first with { WrappedDek = tampered }, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                Require(tamperedResult is null, "Recovery key protector accepted a tampered wrapped DEK.");
                if (tamperedResult is not null) CryptographicOperations.ZeroMemory(tamperedResult);
            }

            char[] checksumTamper = display.ToCharArray();
            int last = checksumTamper.Length - 1;
            checksumTamper[last] = checksumTamper[last] == 'A' ? 'B' : 'A';
            Require(!RecoveryKeyMaterial.TryParse(new string(checksumTamper), out RecoveryKeyMaterial? badChecksum),
                "Recovery key checksum corruption was accepted.");
            badChecksum?.Dispose();
            Array.Clear(checksumTamper, 0, checksumTamper.Length);

            byte[] exported = generated.ExportForExplicitUserAction();
            Require(exported.Length == 32, "Explicit recovery export did not contain exactly 256 bits.");
            CryptographicOperations.ZeroMemory(exported);

            generated.Dispose();
            bool disposedRejected = false;
            try { _ = generated.ToDisplayString(); }
            catch (ObjectDisposedException) { disposedRejected = true; }
            Require(disposedRejected, "Disposed recovery material remained usable.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
