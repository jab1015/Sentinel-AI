using Sentinel.App.Services;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class WindowsCurrentUserProtectionAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        if (!OperatingSystem.IsWindows()) return;

        byte[] dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            WindowsCurrentUserFileKeyProtector protector = new();
            WrappedFileKeyRecord record = protector.WrapAsync(dek, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Require(record.RecordVersion == 1, "Windows current-user key record version changed unexpectedly.");
            Require(record.ProtectionModeId == WindowsCurrentUserFileKeyProtector.ModeId,
                "Windows current-user key record used the wrong protection mode.");
            Require(record.WrappingAlgorithmId == WindowsCurrentUserFileKeyProtector.WrappingAlgorithmDpapiCurrentUser,
                "Windows current-user key record used the wrong wrapping algorithm ID.");
            Require(record.Parameters.Length == 0, "Windows current-user key record unexpectedly persisted auxiliary secret parameters.");
            Require(record.WrappedDek.Length > dek.Length, "Windows current-user protected DEK did not produce a protected blob.");
            Require(!record.WrappedDek.AsSpan().SequenceEqual(dek), "Windows current-user key record stored the raw DEK.");

            byte[]? unwrapped = protector.TryUnwrapAsync(record, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Require(unwrapped is not null && unwrapped.AsSpan().SequenceEqual(dek),
                "Windows current-user DPAPI round trip did not recover the exact DEK.");
            if (unwrapped is not null) CryptographicOperations.ZeroMemory(unwrapped);

            WrappedFileKeyRecord malformed = record with { Parameters = new byte[] { 1 } };
            Require(protector.TryUnwrapAsync(malformed, CancellationToken.None).AsTask().GetAwaiter().GetResult() is null,
                "Windows current-user protector accepted unexpected key-record parameters.");

            byte[] tampered = record.WrappedDek.ToArray();
            tampered[tampered.Length / 2] ^= 0x20;
            WrappedFileKeyRecord tamperedRecord = record with { WrappedDek = tampered };
            byte[]? tamperedResult = protector.TryUnwrapAsync(tamperedRecord, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Require(tamperedResult is null, "Windows current-user protector accepted a tampered DPAPI blob.");
            if (tamperedResult is not null) CryptographicOperations.ZeroMemory(tamperedResult);
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
