using Sentinel.App.Services;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class VaultKeyFoundationAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        byte[] wrappingKey = RandomNumberGenerator.GetBytes(32);
        byte[] wrongWrappingKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            TestKeyProtector protector = new(3, wrappingKey);
            TestKeyProtector wrongProtector = new(3, wrongWrappingKey);
            VaultMasterKeyEnvelope envelope;
            VaultWrappedItemKey wrappedItem;
            byte[] firstItemKey;

            using (SentinelVaultService vault = new())
            {
                envelope = vault.InitializeNewVaultAsync(
                    new IFileKeyProtector[] { protector },
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(vault.State == VaultState.Unlocked && vault.VaultId == envelope.VaultId,
                    "New vault did not enter the explicit unlocked state.");
                Require(envelope.AuthenticationTag.Length == 32 && envelope.WrappedMasterKeys.Count == 1,
                    "Vault master-key envelope was not authenticated/protected as expected.");

                Guid itemId = Guid.NewGuid();
                using VaultItemKeyLease first = vault.CreateItemKey(itemId);
                firstItemKey = first.ItemKey.ToArray();
                wrappedItem = first.WrappedItemKey;

                using VaultItemKeyLease second = vault.CreateItemKey(Guid.NewGuid());
                Require(!firstItemKey.AsSpan().SequenceEqual(second.ItemKey.Span),
                    "Two vault items reused the same plaintext DEK.");

                vault.Lock();
                Require(vault.State == VaultState.Locked && vault.VaultId == Guid.Empty,
                    "Explicit vault lock did not clear the active vault identity/state.");
                bool lockedRejected = false;
                try { using VaultItemKeyLease _ = vault.CreateItemKey(Guid.NewGuid()); }
                catch (InvalidOperationException) { lockedRejected = true; }
                Require(lockedRejected, "Locked vault still allowed a new item key operation.");
            }

            using (SentinelVaultService reopened = new())
            {
                VaultUnlockResult unlocked = reopened.UnlockAsync(
                    envelope,
                    new IFileKeyProtector[] { protector },
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(unlocked.Succeeded && reopened.State == VaultState.Unlocked,
                    "Correct vault key protector did not unlock the authenticated envelope.");

                using VaultItemKeyLease? opened = reopened.TryOpenItemKey(wrappedItem);
                Require(opened is not null && opened.ItemKey.Span.SequenceEqual(firstItemKey),
                    "VMK did not recover the exact independent item DEK.");

                byte[] tamperedWrapped = wrappedItem.WrappedItemKey.ToArray();
                tamperedWrapped[0] ^= 0x01;
                Require(reopened.TryOpenItemKey(wrappedItem with { WrappedItemKey = tamperedWrapped }) is null,
                    "Vault accepted a tampered wrapped item key.");
                Require(reopened.TryOpenItemKey(wrappedItem with { ItemId = Guid.NewGuid() }) is null,
                    "Vault item key was not cryptographically bound to its immutable item ID.");

                Require(reopened.LockIfInactive(TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow.AddSeconds(2)),
                    "Vault inactivity timeout did not lock an unlocked session.");
                Require(reopened.State == VaultState.Locked,
                    "Vault inactivity timeout did not reach Locked state.");
            }

            byte[] tamperedTag = envelope.AuthenticationTag.ToArray();
            tamperedTag[0] ^= 0x80;
            using (SentinelVaultService tamperedVault = new())
            {
                VaultUnlockResult result = tamperedVault.UnlockAsync(
                    envelope with { AuthenticationTag = tamperedTag },
                    new IFileKeyProtector[] { protector },
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(!result.Succeeded && tamperedVault.State == VaultState.Locked,
                    "Vault accepted a tampered master-key envelope authenticator.");
            }

            using (SentinelVaultService wrongVault = new())
            {
                VaultUnlockResult wrong = wrongVault.UnlockAsync(
                    envelope,
                    new IFileKeyProtector[] { wrongProtector },
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(!wrong.Succeeded && wrongVault.State == VaultState.Locked,
                    "Wrong vault credential unlocked the master-key envelope.");
            }

            using (SentinelVaultService sessionLockVault = new())
            {
                Require(sessionLockVault.UnlockAsync(
                    envelope,
                    new IFileKeyProtector[] { protector },
                    CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                    "Session-lock test could not unlock the vault first.");
                sessionLockVault.HandleWindowsSessionLocked();
                Require(sessionLockVault.State == VaultState.Locked,
                    "Windows session-lock hook did not lock the vault key session.");
            }

            VerifyMode4ContainerRoundTrip(protector);

            Console.WriteLine("Vault authenticated VMK envelope / lock state: PASS");
            Console.WriteLine("Vault independent per-item DEK hierarchy: PASS");
            Console.WriteLine("Vault item-ID/tamper binding: PASS");
            Console.WriteLine("Vault inactivity/session lock foundation: PASS");
            Console.WriteLine("Vault mode-4 encrypted-container round trip / lock denial: PASS");
            CryptographicOperations.ZeroMemory(firstItemKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(wrongWrappingKey);
        }
    }

    private static void VerifyMode4ContainerRoundTrip(TestKeyProtector masterProtector)
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultMode4Harness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using SentinelVaultService vault = new();
            VaultMasterKeyEnvelope envelope = vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { masterProtector },
                CancellationToken.None).GetAwaiter().GetResult();

            Guid itemId = Guid.NewGuid();
            VaultFileKeyProtector vaultProtector = VaultFileKeyProtector.ForEncryption(vault, itemId);
            IReadOnlyList<IFileKeyProtector> keys = new IFileKeyProtector[] { vaultProtector };
            FileEncryptionService service = new(SentinelEncryptedContainerV1.MinimumChunkSize);

            string source = Path.Combine(root, "vault-source.bin");
            string container = Path.Combine(root, "vault-item.senc");
            string decrypted = Path.Combine(root, "vault-decrypted.bin");
            byte[] plaintext = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 317);
            File.WriteAllBytes(source, plaintext);

            FileEncryptionResult encryption = service.EncryptAsync(source, container, keys, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(encryption.Succeeded && encryption.Verified,
                "Vault mode-4 encryption was not verified: " + encryption.Code);
            Require(File.Exists(source) && File.ReadAllBytes(source).SequenceEqual(plaintext),
                "Vault encryption modified or removed the source plaintext.");

            FileContainerVerificationResult verified = service.VerifyAsync(container, keys, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(verified.Succeeded, "Fresh Vault mode-4 container did not verify: " + verified.Code);

            FileDecryptionResult decryption = service.DecryptAsync(container, decrypted, keys, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(decryption.Succeeded && File.ReadAllBytes(decrypted).SequenceEqual(plaintext),
                "Vault mode-4 decryption did not reproduce the exact plaintext.");

            vault.Lock();
            FileContainerVerificationResult locked = service.VerifyAsync(container, keys, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!locked.Succeeded && locked.Code == "KeyUnavailable",
                "Locked Vault still released a mode-4 container DEK.");

            Require(vault.UnlockAsync(
                envelope,
                new IFileKeyProtector[] { masterProtector },
                CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Vault mode-4 test could not reopen the same Vault.");

            VaultFileKeyProtector openingProtector = VaultFileKeyProtector.ForOpening(vault, itemId);
            Require(service.VerifyAsync(
                    container,
                    new IFileKeyProtector[] { openingProtector },
                    CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Reopened Vault could not recover the mode-4 container DEK.");

            VaultFileKeyProtector wrongItem = VaultFileKeyProtector.ForOpening(vault, Guid.NewGuid());
            FileContainerVerificationResult wrongItemResult = service.VerifyAsync(
                    container,
                    new IFileKeyProtector[] { wrongItem },
                    CancellationToken.None).GetAwaiter().GetResult();
            Require(!wrongItemResult.Succeeded && wrongItemResult.Code == "KeyUnavailable",
                "A different Vault item ID opened another item's mode-4 container.");

            using SentinelVaultService differentVault = new();
            _ = differentVault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { masterProtector },
                CancellationToken.None).GetAwaiter().GetResult();
            VaultFileKeyProtector wrongVault = VaultFileKeyProtector.ForOpening(differentVault, itemId);
            FileContainerVerificationResult wrongVaultResult = service.VerifyAsync(
                    container,
                    new IFileKeyProtector[] { wrongVault },
                    CancellationToken.None).GetAwaiter().GetResult();
            Require(!wrongVaultResult.Succeeded && wrongVaultResult.Code == "KeyUnavailable",
                "A different Vault identity opened a mode-4 container.");

            CryptographicOperations.ZeroMemory(plaintext);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
