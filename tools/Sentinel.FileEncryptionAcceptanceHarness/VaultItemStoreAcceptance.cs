using Sentinel.App.Services;
using System.Security.Cryptography;

internal static class VaultItemStoreAcceptance
{
    internal static void Verify()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultItemStoreHarness", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(Path.GetTempPath(), "SentinelVaultItemSourceHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sourceRoot);
        byte[] masterWrappingKey = RandomNumberGenerator.GetBytes(32);
        byte[] plaintext = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 911);

        try
        {
            TestKeyProtector masterProtector = new(3, masterWrappingKey);
            using SentinelVaultService vault = new();
            _ = vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { masterProtector },
                CancellationToken.None).GetAwaiter().GetResult();

            string source = Path.Combine(sourceRoot, "original.bin");
            File.WriteAllBytes(source, plaintext);
            string sourceHash = Convert.ToHexString(SHA256.HashData(plaintext));

            VaultItemStoreService store = new(
                root,
                vault,
                new FileEncryptionService(SentinelEncryptedContainerV1.MinimumChunkSize));

            VaultAddItemResult added = store.AddFileAsync(source, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(added.Succeeded && !added.RecoveryRequired && added.Code == "VerifiedCommitted",
                "Vault item transaction did not reach verified completion: " + added.Code);
            Require(added.ItemId != Guid.Empty && added.TransactionId != Guid.Empty,
                "Vault item transaction did not preserve stable item/transaction identities.");
            Require(File.Exists(added.CiphertextPath),
                "Verified Vault item ciphertext was not present after completion.");
            Require(File.Exists(source),
                "Vault add-item transaction removed the source plaintext.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == sourceHash,
                "Vault add-item transaction modified the source plaintext.");

            VaultMetadataStore metadata = new(root, vault);
            VaultMetadataReadResult read = metadata.ReadSnapshotAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(read.Succeeded && read.Snapshot is not null,
                "Completed Vault transaction did not leave readable authenticated metadata.");
            Require(read.Snapshot!.PendingTransactions.Count == 0,
                "Completed Vault transaction left a pending transaction record.");
            Require(read.Snapshot.Items.Count == 1 && read.Snapshot.Items[0].ItemId == added.ItemId,
                "Completed Vault transaction did not commit exactly one expected item record.");
            Require(read.Snapshot.Items[0].PlaintextBytes == plaintext.Length,
                "Vault metadata did not preserve the verified plaintext length.");
            Require(string.Equals(
                    read.Snapshot.Items[0].CiphertextFileName,
                    Path.GetFileName(added.CiphertextPath),
                    StringComparison.Ordinal),
                "Vault metadata did not bind the committed item to its ciphertext filename.");

            VaultFileKeyProtector openingProtector = VaultFileKeyProtector.ForOpening(vault, added.ItemId);
            FileContainerVerificationResult verified = new FileEncryptionService(SentinelEncryptedContainerV1.MinimumChunkSize)
                .VerifyAsync(
                    added.CiphertextPath,
                    new IFileKeyProtector[] { openingProtector },
                    CancellationToken.None).GetAwaiter().GetResult();
            Require(verified.Succeeded && verified.PlaintextBytes == plaintext.Length,
                "Committed Vault ciphertext could not be independently authenticated from metadata identity.");

            string secondSource = Path.Combine(sourceRoot, "second.bin");
            byte[] secondPlaintext = RandomNumberGenerator.GetBytes(333);
            File.WriteAllBytes(secondSource, secondPlaintext);
            try
            {
                VaultAddItemResult second = store.AddFileAsync(secondSource, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Require(second.Succeeded && second.ItemId != added.ItemId,
                    "A second serialized Vault transaction did not commit independently.");

                VaultMetadataReadResult secondRead = metadata.ReadSnapshotAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                Require(secondRead.Succeeded && secondRead.Snapshot is not null &&
                        secondRead.Snapshot.Items.Count == 2 &&
                        secondRead.Snapshot.PendingTransactions.Count == 0,
                    "Second Vault transaction did not preserve both committed items with no pending state.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secondPlaintext);
            }

            VerifyPreparedRollbackRecovery(masterProtector);
            VerifyPreparedCiphertextResume(masterProtector, sourceRoot);

            vault.Lock();
            string lockedSource = Path.Combine(sourceRoot, "locked.bin");
            File.WriteAllText(lockedSource, "must remain outside the locked vault");
            VaultAddItemResult locked = store.AddFileAsync(lockedSource, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!locked.Succeeded && locked.Code == "VaultLocked",
                "Locked Vault accepted a new item transaction.");

            Console.WriteLine("Vault durable add-item transaction: PASS");
            Console.WriteLine("Vault source-preservation contract: PASS");
            Console.WriteLine("Vault serialized multi-item metadata commit: PASS");
            Console.WriteLine("Vault Prepared/no-ciphertext crash rollback: PASS");
            Console.WriteLine("Vault Prepared/verified-ciphertext crash resume: PASS");
            Console.WriteLine("Vault locked add-item refusal: PASS");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterWrappingKey);
            CryptographicOperations.ZeroMemory(plaintext);
            TryDeleteTree(root);
            TryDeleteTree(sourceRoot);
        }
    }

    private static void VerifyPreparedRollbackRecovery(TestKeyProtector masterProtector)
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultRollbackHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using SentinelVaultService vault = new();
            _ = vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { masterProtector },
                CancellationToken.None).GetAwaiter().GetResult();
            VaultItemStoreService store = new(
                root,
                vault,
                new FileEncryptionService(SentinelEncryptedContainerV1.MinimumChunkSize));
            VaultMetadataStore metadata = new(root, vault);

            Guid itemId = Guid.NewGuid();
            Guid transactionId = Guid.NewGuid();
            VaultWrappedItemKey wrapped;
            using (VaultItemKeyLease key = vault.CreateItemKey(itemId))
            {
                VaultWrappedItemKey source = key.WrappedItemKey;
                wrapped = new VaultWrappedItemKey(
                    source.Version,
                    source.VaultId,
                    source.ItemId,
                    source.Nonce.ToArray(),
                    source.WrappedItemKey.ToArray());
            }

            VaultMetadataSnapshot prepared = new(
                1,
                vault.VaultId,
                0,
                new[] { new VaultMetadataItem(itemId, itemId.ToString("N") + ".senc", 42, wrapped) },
                new[]
                {
                    new VaultTransactionRecord(
                        transactionId,
                        itemId,
                        VaultTransactionState.Prepared,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                });
            Require(metadata.WriteSnapshotAsync(prepared, CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Prepared rollback fixture could not persist its transaction.");

            VaultRecoveryResult recovery = store.RecoverPendingAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(recovery.Succeeded && recovery.RecoveredTransactions == 1,
                "Prepared transaction without ciphertext was not safely rolled back: " + recovery.Code);

            VaultMetadataReadResult after = metadata.ReadSnapshotAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(after.Succeeded && after.Snapshot is not null &&
                    after.Snapshot.Items.Count == 0 &&
                    after.Snapshot.PendingTransactions.Count == 0,
                "Prepared rollback did not remove only the incomplete item metadata/transaction.");
        }
        finally
        {
            TryDeleteTree(root);
        }
    }

    private static void VerifyPreparedCiphertextResume(TestKeyProtector masterProtector, string sourceRoot)
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultResumeHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] plaintext = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 77);
        try
        {
            using SentinelVaultService vault = new();
            _ = vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { masterProtector },
                CancellationToken.None).GetAwaiter().GetResult();
            VaultItemStoreService store = new(
                root,
                vault,
                new FileEncryptionService(SentinelEncryptedContainerV1.MinimumChunkSize));
            VaultMetadataStore metadata = new(root, vault);

            Guid itemId = Guid.NewGuid();
            Guid transactionId = Guid.NewGuid();
            string fileName = itemId.ToString("N") + ".senc";
            string ciphertextPath = Path.Combine(root, "items", fileName);
            string sourcePath = Path.Combine(sourceRoot, "resume-" + itemId.ToString("N") + ".bin");
            File.WriteAllBytes(sourcePath, plaintext);

            using VaultItemKeyLease itemKey = vault.CreateItemKey(itemId);
            VaultWrappedItemKey sourceWrapped = itemKey.WrappedItemKey;
            VaultWrappedItemKey wrapped = new(
                sourceWrapped.Version,
                sourceWrapped.VaultId,
                sourceWrapped.ItemId,
                sourceWrapped.Nonce.ToArray(),
                sourceWrapped.WrappedItemKey.ToArray());

            VaultMetadataSnapshot prepared = new(
                1,
                vault.VaultId,
                0,
                new[] { new VaultMetadataItem(itemId, fileName, plaintext.Length, wrapped) },
                new[]
                {
                    new VaultTransactionRecord(
                        transactionId,
                        itemId,
                        VaultTransactionState.Prepared,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                });
            Require(metadata.WriteSnapshotAsync(prepared, CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Prepared resume fixture could not persist its transaction.");

            using VaultFileKeyProtector protector = VaultFileKeyProtector.ForEncryption(vault, itemKey);
            FileEncryptionResult encrypted = new FileEncryptionService(SentinelEncryptedContainerV1.MinimumChunkSize)
                .EncryptAsync(
                    sourcePath,
                    ciphertextPath,
                    new IFileKeyProtector[] { protector },
                    CancellationToken.None).GetAwaiter().GetResult();
            Require(encrypted.Succeeded && encrypted.Verified,
                "Prepared resume fixture could not create a verified ciphertext.");

            VaultRecoveryResult recovery = store.RecoverPendingAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(recovery.Succeeded && recovery.RecoveredTransactions == 1,
                "Verified Prepared ciphertext was not resumed through completion: " + recovery.Code);

            VaultMetadataReadResult after = metadata.ReadSnapshotAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(after.Succeeded && after.Snapshot is not null &&
                    after.Snapshot.PendingTransactions.Count == 0 &&
                    after.Snapshot.Items.Count == 1 &&
                    after.Snapshot.Items[0].ItemId == itemId,
                "Crash-resumed item did not finish with committed metadata and no pending transaction.");
            Require(File.Exists(sourcePath) && File.ReadAllBytes(sourcePath).SequenceEqual(plaintext),
                "Crash recovery modified or removed the original plaintext.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            TryDeleteTree(root);
        }
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
