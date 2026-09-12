using Sentinel.App.Services;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class VaultUnlockLockRaceAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        byte[] wrappingKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            TestKeyProtector inner = new(3, wrappingKey);
            VaultMasterKeyEnvelope envelope;
            using (SentinelVaultService creator = new())
            {
                envelope = creator.InitializeNewVaultAsync(
                    new IFileKeyProtector[] { inner },
                    CancellationToken.None).GetAwaiter().GetResult();
                creator.Lock();
            }

            BlockingProtector blocking = new(inner);
            using SentinelVaultService vault = new();
            Task<VaultUnlockResult> unlockTask = vault.UnlockAsync(
                envelope,
                new IFileKeyProtector[] { blocking },
                CancellationToken.None);

            Require(blocking.WaitUntilUnwrapEntered(TimeSpan.FromSeconds(5)),
                "Vault unlock race harness did not reach the controlled unwrap point.");
            Require(vault.State == VaultState.Unlocking,
                "Vault was not in Unlocking state at the controlled race point.");

            vault.HandleWindowsSessionLocked();
            Require(vault.State == VaultState.Locked,
                "Windows session lock did not immediately force Locked state during unlock.");

            blocking.Release();
            VaultUnlockResult result = unlockTask.GetAwaiter().GetResult();
            Require(!result.Succeeded && result.Code == "LockStateChanged",
                "An in-flight unlock survived a Windows session-lock transition.");
            Require(vault.State == VaultState.Locked && vault.VaultId == Guid.Empty,
                "Vault became unlocked after the session-lock race completed.");

            Console.WriteLine("Vault in-flight unlock invalidation on session lock: PASS");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class BlockingProtector : IFileKeyProtector
    {
        private readonly IFileKeyProtector _inner;
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal BlockingProtector(IFileKeyProtector inner) => _inner = inner;

        public ushort ProtectionModeId => _inner.ProtectionModeId;

        public ValueTask<WrappedFileKeyRecord> WrapAsync(ReadOnlyMemory<byte> dataEncryptionKey, CancellationToken cancellationToken) =>
            _inner.WrapAsync(dataEncryptionKey, cancellationToken);

        public async ValueTask<byte[]?> TryUnwrapAsync(WrappedFileKeyRecord record, CancellationToken cancellationToken)
        {
            _entered.Set();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await _inner.TryUnwrapAsync(record, cancellationToken).ConfigureAwait(false);
        }

        internal bool WaitUntilUnwrapEntered(TimeSpan timeout) => _entered.Wait(timeout);
        internal void Release() => _release.TrySetResult(true);
    }
}
