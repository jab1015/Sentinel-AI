using Sentinel.App.Services;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class VaultMetadataAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        VerifyAsync().GetAwaiter().GetResult();
    }

    private static async Task VerifyAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultMetadataHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] master = RandomNumberGenerator.GetBytes(32);
        try
        {
            TestKeyProtector protector = new(3, master);
            IReadOnlyList<IFileKeyProtector> protectors = new IFileKeyProtector[] { protector };
            using SentinelVaultService vault = new();
            VaultMasterKeyEnvelope envelope = await vault.InitializeNewVaultAsync(protectors);
            VaultMetadataStore store = new(root, vault);

            Guid itemId = Guid.NewGuid();
            VaultWrappedItemKey wrappedItemKey;
            using (VaultItemKeyLease itemKey = vault.CreateItemKey(itemId))
                wrappedItemKey = itemKey.WrappedItemKey;

            Guid transactionId = Guid.NewGuid();
            VaultMetadataSnapshot generation0 = new(
                1,
                envelope.VaultId,
                0,
                new[] { new VaultMetadataItem(itemId, $"{itemId:N}.svlt", 1234, wrappedItemKey) },
                new[] { new VaultTransactionRecord(transactionId, itemId, VaultTransactionState.Prepared, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) });

            VaultMetadataWriteResult firstWrite = await store.WriteSnapshotAsync(generation0);
            Require(firstWrite.Succeeded && firstWrite.Generation == 0,
                "Vault metadata generation 0 did not commit and verify.");

            VaultMetadataReadResult firstRead = await store.ReadSnapshotAsync();
            Require(firstRead.Succeeded && firstRead.Snapshot is not null,
                "Committed vault metadata could not be reopened and authenticated.");
            Require(firstRead.Snapshot!.Generation == 0 && firstRead.Snapshot.PendingTransactions.Count == 1 &&
                    firstRead.Snapshot.PendingTransactions[0].State == VaultTransactionState.Prepared,
                "Prepared recovery transaction did not survive authenticated metadata persistence.");

            VaultMetadataWriteResult rollback = await store.WriteSnapshotAsync(generation0);
            Require(!rollback.Succeeded && rollback.Code == "GenerationConflict",
                "Vault metadata accepted a rollback/duplicate generation.");

            VaultMetadataSnapshot generation1 = generation0 with
            {
                Generation = 1,
                PendingTransactions = new[]
                {
                    generation0.PendingTransactions[0] with
                    {
                        State = VaultTransactionState.CiphertextReady,
                        UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                }
            };

            string leasePath = Path.Combine(root, ".vault-writer.lock");
            using (FileStream conflictingLease = new(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                VaultMetadataWriteResult contention = await store.WriteSnapshotAsync(generation1);
                Require(!contention.Succeeded && contention.Code == "IoFailure",
                    "Vault metadata writer lease did not reject a concurrent writer.");
            }

            VaultMetadataWriteResult secondWrite = await store.WriteSnapshotAsync(generation1);
            Require(secondWrite.Succeeded && secondWrite.Generation == 1,
                "Vault metadata generation 1 did not commit after writer lease release.");

            VaultMetadataReadResult secondRead = await store.ReadSnapshotAsync();
            Require(secondRead.Succeeded && secondRead.Snapshot is not null &&
                    secondRead.Snapshot.PendingTransactions[0].State == VaultTransactionState.CiphertextReady,
                "CiphertextReady recovery state did not survive authenticated metadata persistence.");

            string metadataPath = Path.Combine(root, "vault-metadata.svmd");
            byte[] committed = File.ReadAllBytes(metadataPath);
            byte[] tampered = committed.ToArray();
            tampered[^1] ^= 0x80;
            File.WriteAllBytes(metadataPath, tampered);
            VaultMetadataReadResult tamperedRead = await store.ReadSnapshotAsync();
            Require(!tamperedRead.Succeeded && tamperedRead.Code == "AuthenticationFailed",
                "Tampered Vault metadata was accepted.");
            File.WriteAllBytes(metadataPath, committed);

            vault.Lock();
            VaultMetadataReadResult lockedRead = await store.ReadSnapshotAsync();
            Require(!lockedRead.Succeeded && lockedRead.Code == "VaultLocked",
                "Locked Vault allowed metadata decryption.");

            VaultUnlockResult unlocked = await vault.UnlockAsync(envelope, protectors);
            Require(unlocked.Succeeded, "Vault could not unlock after metadata lock test.");
            VaultMetadataReadResult reopened = await store.ReadSnapshotAsync();
            Require(reopened.Succeeded && reopened.Snapshot?.Generation == 1,
                "Vault metadata did not reopen after a valid lock/unlock cycle.");

            Console.WriteLine("Vault durable metadata generation ordering: PASS");
            Console.WriteLine("Vault transaction/recovery state persistence: PASS");
            Console.WriteLine("Vault writer lease contention: PASS");
            Console.WriteLine("Vault metadata tamper rejection: PASS");
            Console.WriteLine("Vault locked-state metadata denial: PASS");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
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
