using Sentinel.App.Services;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class VaultItemStoreAcceptance
{
    [ModuleInitializer]
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
            Require(read.Snapshot.PendingTransactions.Count == 0,
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
