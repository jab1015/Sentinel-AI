using Sentinel.App.Services;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class VaultExportAcceptance
{
    private const long ConcurrentExportFixtureBytes = 128L * 1024 * 1024;

    [ModuleInitializer]
    internal static void Verify()
    {
        string vaultRoot = Path.Combine(Path.GetTempPath(), "SentinelVaultExportHarness", Guid.NewGuid().ToString("N"));
        string externalRoot = Path.Combine(Path.GetTempPath(), "SentinelVaultExportOutput", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(vaultRoot);
        Directory.CreateDirectory(externalRoot);
        byte[] wrappingKey = RandomNumberGenerator.GetBytes(32);
        byte[] plaintext = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 515);

        try
        {
            TestKeyProtector protector = new(3, wrappingKey);
            using SentinelVaultService vault = new();
            VaultMasterKeyEnvelope envelope = vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { protector },
                CancellationToken.None).GetAwaiter().GetResult();

            FileEncryptionService encryption = new(SentinelEncryptedContainerV1.MinimumChunkSize);
            VaultItemStoreService store = new(vaultRoot, vault, encryption);
            VaultItemExportService exporter = new(vaultRoot, vault, store, encryption);

            string source = Path.Combine(externalRoot, "source.bin");
            File.WriteAllBytes(source, plaintext);
            VaultAddItemResult added = store.AddFileAsync(source, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(added.Succeeded, "Export fixture could not commit a Vault item: " + added.Code);

            string metadataPath = Path.Combine(vaultRoot, "vault-metadata.svmd");
            byte[] metadataBefore = SHA256.HashData(File.ReadAllBytes(metadataPath));
            byte[] ciphertextBefore = SHA256.HashData(File.ReadAllBytes(added.CiphertextPath));

            string destination = Path.Combine(externalRoot, "exported.bin");
            VaultExportResult exported = exporter.ExportAsync(added.ItemId, destination, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(exported.Succeeded && exported.Code == "VerifiedExported",
                "Committed Vault item did not export successfully: " + exported.Code);
            Require(exported.OutputRemains && exported.PlaintextBytes == plaintext.Length,
                "Successful Vault export did not report the durable plaintext output correctly.");
            Require(File.Exists(destination) && File.ReadAllBytes(destination).SequenceEqual(plaintext),
                "Vault export did not reproduce the exact plaintext.");
            Require(SHA256.HashData(File.ReadAllBytes(metadataPath)).SequenceEqual(metadataBefore),
                "Vault export mutated authenticated Vault metadata.");
            Require(SHA256.HashData(File.ReadAllBytes(added.CiphertextPath)).SequenceEqual(ciphertextBefore),
                "Vault export mutated or replaced the Vault ciphertext.");

            VaultExportResult collision = exporter.ExportAsync(added.ItemId, destination, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!collision.Succeeded && collision.Code == "DestinationCollision",
                "Vault export overwrote an existing destination.");
            Require(File.ReadAllBytes(destination).SequenceEqual(plaintext),
                "Collision refusal changed the existing destination.");

            string insideVault = Path.Combine(vaultRoot, "plaintext-export.bin");
            VaultExportResult inside = exporter.ExportAsync(added.ItemId, insideVault, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!inside.Succeeded && inside.Code == "DestinationInsideVault" && !File.Exists(insideVault),
                "Vault export allowed plaintext to be written inside Vault storage.");

            string unknownDestination = Path.Combine(externalRoot, "unknown.bin");
            VaultExportResult unknown = exporter.ExportAsync(Guid.NewGuid(), unknownDestination, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!unknown.Succeeded && unknown.Code == "ItemNotCommitted" && !File.Exists(unknownDestination),
                "Unknown Vault item identity produced plaintext output.");

            string lockedDestination = Path.Combine(externalRoot, "locked.bin");
            vault.Lock();
            VaultExportResult locked = exporter.ExportAsync(added.ItemId, lockedDestination, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!locked.Succeeded && locked.Code == "VaultLocked" && !File.Exists(lockedDestination),
                "Locked Vault exported plaintext.");

            Require(vault.UnlockAsync(
                envelope,
                new IFileKeyProtector[] { protector },
                CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Export fixture could not reopen the Vault.");

            VerifyLockRevokesInFlightExport(vault, envelope, protector, store, exporter, externalRoot);

            VaultMetadataStore metadata = new(vaultRoot, vault);
            VaultMetadataReadResult current = metadata.ReadSnapshotAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(current.Succeeded && current.Snapshot is not null,
                "Export pending-item fixture could not read metadata.");
            VaultMetadataSnapshot pending = new(
                current.Snapshot!.Version,
                current.Snapshot.VaultId,
                checked(current.Snapshot.Generation + 1),
                current.Snapshot.Items.ToArray(),
                new[]
                {
                    new VaultTransactionRecord(
                        Guid.NewGuid(),
                        added.ItemId,
                        VaultTransactionState.MetadataCommitted,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                });
            Require(metadata.WriteSnapshotAsync(pending, CancellationToken.None).GetAwaiter().GetResult().Succeeded,
                "Export pending-item fixture could not persist pending state.");

            string pendingDestination = Path.Combine(externalRoot, "pending.bin");
            VaultExportResult pendingResult = exporter.ExportAsync(added.ItemId, pendingDestination, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(!pendingResult.Succeeded && pendingResult.Code == "ItemNotCommitted" && !File.Exists(pendingDestination),
                "Pending Vault item was exported as committed content.");

            Require(File.Exists(source) && File.ReadAllBytes(source).SequenceEqual(plaintext),
                "Vault export testing modified or removed the original source plaintext.");
            Require(File.Exists(added.CiphertextPath),
                "Vault export testing removed the Vault ciphertext.");

            Console.WriteLine("Vault exact committed-item export: PASS");
            Console.WriteLine("Vault export metadata/ciphertext immutability: PASS");
            Console.WriteLine("Vault export collision / internal-destination refusal: PASS");
            Console.WriteLine("Vault locked / unknown / pending item refusal: PASS");
            Console.WriteLine("Vault lock revokes in-flight export after DEK unwrap and removes exact-owned plaintext: PASS");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(plaintext);
            TryDeleteTree(vaultRoot);
            TryDeleteTree(externalRoot);
        }
    }

    private static void VerifyLockRevokesInFlightExport(
        SentinelVaultService vault,
        VaultMasterKeyEnvelope envelope,
        TestKeyProtector protector,
        VaultItemStoreService store,
        VaultItemExportService exporter,
        string externalRoot)
    {
        string largeSource = Path.Combine(externalRoot, "lock-transition-source.bin");
        string interruptedDestination = Path.Combine(externalRoot, "lock-transition-export.bin");
        WriteBoundedLargeFixture(largeSource, ConcurrentExportFixtureBytes);

        VaultAddItemResult largeAdded = store.AddFileAsync(largeSource, CancellationToken.None)
            .GetAwaiter().GetResult();
        Require(largeAdded.Succeeded, "Lock-transition fixture could not commit a Vault item: " + largeAdded.Code);
        Require(File.Exists(largeAdded.CiphertextPath), "Lock-transition fixture ciphertext was not durable before export.");

        Task<VaultExportResult> inFlight = exporter.ExportAsync(
            largeAdded.ItemId,
            interruptedDestination,
            CancellationToken.None);

        WaitForPlaintextOutputCreation(interruptedDestination, inFlight, TimeSpan.FromSeconds(30));
        vault.Lock();

        VaultExportResult interrupted = inFlight.WaitAsync(TimeSpan.FromSeconds(30))
            .GetAwaiter().GetResult();
        Require(!interrupted.Succeeded,
            "Vault lock transition allowed an in-flight plaintext export to report success.");
        Require(interrupted.Code == "VaultLockTransition",
            "Vault lock transition did not complete exact-owned plaintext cleanup: " + interrupted.Code);
        Require(!interrupted.OutputRemains && !File.Exists(interruptedDestination),
            "Vault lock transition left Sentinel-owned incomplete plaintext behind.");
        Require(File.Exists(largeAdded.CiphertextPath),
            "Vault lock transition removed or lost the Vault ciphertext.");
        Require(File.Exists(largeSource) && new FileInfo(largeSource).Length == ConcurrentExportFixtureBytes,
            "Vault lock transition modified the original source fixture.");

        Require(vault.UnlockAsync(
            envelope,
            new IFileKeyProtector[] { protector },
            CancellationToken.None).GetAwaiter().GetResult().Succeeded,
            "Vault could not reopen after the in-flight export lock transition.");
    }

    private static void WaitForPlaintextOutputCreation(
        string destination,
        Task exportTask,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            // FileEncryptionService unwraps/authenticates the container DEK before it creates
            // the plaintext output. Observing this exact destination therefore proves the
            // lock transition occurs after DEK recovery without relying on share-sensitive
            // file-length metadata or an arbitrary timing delay.
            if (File.Exists(destination))
                return;
            if (exportTask.IsCompleted)
                break;
            Thread.Sleep(1);
        }

        if (exportTask.IsFaulted)
            exportTask.GetAwaiter().GetResult();
        throw new InvalidOperationException(
            exportTask.IsCompleted
                ? "In-flight export completed before the harness observed plaintext output creation; revocation was not exercised."
                : "Timed out waiting for an in-flight plaintext export to create its owned output.");
    }

    private static void WriteBoundedLargeFixture(string path, long length)
    {
        byte[] block = new byte[1024 * 1024];
        RandomNumberGenerator.Fill(block);
        try
        {
            using FileStream stream = new(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                block.Length,
                FileOptions.SequentialScan);
            long remaining = length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(block.Length, remaining);
                stream.Write(block, 0, count);
                remaining -= count;
            }
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(block);
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
