using Sentinel.App.Services;
using System.Security.Cryptography;

internal static class VaultBoundaryAcceptance
{
    internal static void Verify()
    {
        byte[] masterWrappingKey = RandomNumberGenerator.GetBytes(32);
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultBoundaryHarness", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(Path.GetTempPath(), "SentinelVaultBoundarySource", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sourceRoot);
        try
        {
            TestKeyProtector protector = new(3, masterWrappingKey);
            VerifyMissingItemsBoundaryFailsClosed(root, sourceRoot, protector);
            VerifyPendingItemsAreNotSurfaced(protector);
            VerifyDuplicateCiphertextIdentityFailsClosed(protector);

            Console.WriteLine("Vault operation-time storage-boundary revalidation: PASS");
            Console.WriteLine("Vault pending-item visibility filtering: PASS");
            Console.WriteLine("Vault ambiguous authenticated metadata rejection: PASS");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterWrappingKey);
            TryDeleteTree(root);
            TryDeleteTree(sourceRoot);
        }
    }

    private static void VerifyMissingItemsBoundaryFailsClosed(
        string root,
        string sourceRoot,
        TestKeyProtector protector)
    {
        using SentinelVaultService vault = new();
        _ = vault.InitializeNewVaultAsync(
            new IFileKeyProtector[] { protector },
            CancellationToken.None).GetAwaiter().GetResult();

        VaultItemStoreService store = new(
            root,
            vault,
            new FileEncryptionService(SentinelEncryptedContainerV1.MinimumChunkSize));

        string items = Path.Combine(root, "items");
        Directory.Delete(items);
        string source = Path.Combine(sourceRoot, "boundary-source.bin");
        File.WriteAllBytes(source, RandomNumberGenerator.GetBytes(64));

        VaultAddItemResult result = store.AddFileAsync(source, CancellationToken.None)
            .GetAwaiter().GetResult();
        Require(!result.Succeeded && result.Code == "StorageBoundaryMissing",
            "Vault recreated or followed a changed items boundary instead of failing closed: " + result.Code);
        Require(File.Exists(source), "Storage-boundary rejection modified or removed the source file.");
    }

    private static void VerifyPendingItemsAreNotSurfaced(TestKeyProtector protector)
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultVisibilityHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using SentinelVaultService vault = new();
            _ = vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { protector },
                CancellationToken.None).GetAwaiter().GetResult();
            VaultMetadataStore metadata = new(root, vault);
            VaultItemStoreService store = new(
                root,
                vault,
                new FileEncryptionService(SentinelEncryptedContainerV1.MinimumChunkSize));

            Guid committedId = Guid.NewGuid();
            Guid pendingId = Guid.NewGuid();
            VaultWrappedItemKey committedKey;
            VaultWrappedItemKey pendingKey;
            using (VaultItemKeyLease lease = vault.CreateItemKey(committedId))
                committedKey = Clone(lease.WrappedItemKey);
            using (VaultItemKeyLease lease = vault.CreateItemKey(pendingId))
                pendingKey = Clone(lease.WrappedItemKey);

            VaultMetadataSnapshot snapshot = new(
                1,
                vault.VaultId,
                0,
                new[]
                {
                    new VaultMetadataItem(committedId, committedId.ToString("N") + ".senc", 10, committedKey),
                    new VaultMetadataItem(pendingId, pendingId.ToString("N") + ".senc", 20, pendingKey)
                },
                new[]
                {
                    new VaultTransactionRecord(
                        Guid.NewGuid(),
                        pendingId,
                        VaultTransactionState.Prepared,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                });
            Require(metadata.WriteSnapshotAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Visibility fixture metadata could not be persisted.");

            VaultCommittedItemsResult listed = store.ListCommittedItemsAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(listed.Succeeded && listed.Items.Count == 1 && listed.Items[0].ItemId == committedId,
                "Pending Vault item was surfaced as committed content.");
            Require(listed.Items.All(i => i.ItemId != pendingId),
                "Pending Vault item leaked into the committed item view.");
        }
        finally
        {
            TryDeleteTree(root);
        }
    }

    private static void VerifyDuplicateCiphertextIdentityFailsClosed(TestKeyProtector protector)
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelVaultDuplicateHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using SentinelVaultService vault = new();
            _ = vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { protector },
                CancellationToken.None).GetAwaiter().GetResult();
            VaultMetadataStore metadata = new(root, vault);
            VaultItemStoreService store = new(root, vault);

            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            VaultWrappedItemKey firstKey;
            VaultWrappedItemKey secondKey;
            using (VaultItemKeyLease lease = vault.CreateItemKey(firstId))
                firstKey = Clone(lease.WrappedItemKey);
            using (VaultItemKeyLease lease = vault.CreateItemKey(secondId))
                secondKey = Clone(lease.WrappedItemKey);

            string duplicateFilename = "shared-ciphertext.senc";
            VaultMetadataSnapshot duplicate = new(
                1,
                vault.VaultId,
                0,
                new[]
                {
                    new VaultMetadataItem(firstId, duplicateFilename, 1, firstKey),
                    new VaultMetadataItem(secondId, duplicateFilename, 1, secondKey)
                },
                Array.Empty<VaultTransactionRecord>());
            Require(metadata.WriteSnapshotAsync(duplicate, CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Writer-valid duplicate ciphertext fixture could not be authenticated/persisted.");

            VaultCommittedItemsResult result = store.ListCommittedItemsAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!result.Succeeded && result.Code == "DuplicateOrInvalidCiphertextIdentity",
                "Ambiguous authenticated ciphertext identities were accepted: " + result.Code);
        }
        finally
        {
            TryDeleteTree(root);
        }
    }

    private static VaultWrappedItemKey Clone(VaultWrappedItemKey record) =>
        new(record.Version, record.VaultId, record.ItemId, record.Nonce.ToArray(), record.WrappedItemKey.ToArray());

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
