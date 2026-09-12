using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class VaultItemStoreService
{
    private const int MetadataVersion = 1;
    private const string ItemsDirectoryName = "items";
    private const string OperationLeaseFileName = ".vault-item-operation.lock";

    private readonly string _root;
    private readonly string _itemsRoot;
    private readonly string _operationLeasePath;
    private readonly SentinelVaultService _vault;
    private readonly VaultMetadataStore _metadata;
    private readonly FileEncryptionService _encryption;

    internal VaultItemStoreService(
        string root,
        SentinelVaultService vault,
        FileEncryptionService? encryption = null)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("A Vault storage root is required.", nameof(root));

        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        RejectReparseDirectory(_root, "Vault storage root");

        _itemsRoot = Path.Combine(_root, ItemsDirectoryName);
        Directory.CreateDirectory(_itemsRoot);
        RejectReparseDirectory(_itemsRoot, "Vault item directory");

        _operationLeasePath = Path.Combine(_root, OperationLeaseFileName);
        _metadata = new VaultMetadataStore(_root, _vault);
        _encryption = encryption ?? new FileEncryptionService();
    }

    internal async Task<VaultAddItemResult> AddFileAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (_vault.State != VaultState.Unlocked || _vault.VaultId == Guid.Empty)
            return VaultAddItemResult.Fail("VaultLocked");

        if (!TryValidateSource(sourcePath, out string source, out long plaintextBytes, out string sourceError))
            return VaultAddItemResult.Fail(sourceError);

        await using FileStream? operationLease = await TryAcquireOperationLeaseAsync(cancellationToken).ConfigureAwait(false);
        if (operationLease is null)
            return VaultAddItemResult.Fail("OperationLeaseUnavailable");

        Guid vaultId = _vault.VaultId;
        if (_vault.State != VaultState.Unlocked || vaultId == Guid.Empty)
            return VaultAddItemResult.Fail("VaultLocked");

        VaultMetadataReadResult read = await _metadata.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        VaultMetadataSnapshot current;
        if (read.Succeeded && read.Snapshot is not null)
        {
            current = read.Snapshot;
            if (current.VaultId != vaultId)
                return VaultAddItemResult.Fail("VaultIdentityMismatch");
            if (current.PendingTransactions.Count != 0)
                return VaultAddItemResult.Fail("RecoveryRequired");
        }
        else if (read.Code == "NotFound")
        {
            current = new VaultMetadataSnapshot(
                MetadataVersion,
                vaultId,
                -1,
                Array.Empty<VaultMetadataItem>(),
                Array.Empty<VaultTransactionRecord>());
        }
        else
        {
            return VaultAddItemResult.Fail("MetadataUnavailable:" + read.Code);
        }

        Guid itemId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        string ciphertextFileName = itemId.ToString("N") + ".senc";
        string ciphertextPath = Path.Combine(_itemsRoot, ciphertextFileName);
        if (File.Exists(ciphertextPath) || Directory.Exists(ciphertextPath))
            return VaultAddItemResult.Fail("CiphertextCollision", itemId, transactionId, ciphertextPath, plaintextBytes);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        VaultTransactionRecord transaction = new(
            transactionId,
            itemId,
            VaultTransactionState.Prepared,
            now);

        VaultMetadataSnapshot prepared = NextSnapshot(
            current,
            current.Items,
            new[] { transaction });
        VaultMetadataWriteResult preparedWrite = await _metadata.WriteSnapshotAsync(prepared, cancellationToken).ConfigureAwait(false);
        if (!preparedWrite.Succeeded)
            return VaultAddItemResult.Fail("PrepareMetadataFailed:" + preparedWrite.Code, itemId, transactionId, ciphertextPath, plaintextBytes);
        current = prepared;

        try
        {
            using VaultItemKeyLease itemKey = _vault.CreateItemKey(itemId);
            VaultWrappedItemKey wrappedItemKey = CloneWrappedItemKey(itemKey.WrappedItemKey);
            using VaultFileKeyProtector protector = VaultFileKeyProtector.ForEncryption(_vault, itemKey);
            IReadOnlyList<IFileKeyProtector> protectors = new IFileKeyProtector[] { protector };

            FileEncryptionResult encrypted = await _encryption.EncryptAsync(
                source,
                ciphertextPath,
                protectors,
                cancellationToken).ConfigureAwait(false);
            if (!encrypted.Succeeded || !encrypted.Verified)
                return VaultAddItemResult.Fail(
                    "CiphertextCreationFailed:" + encrypted.Code,
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);

            FileContainerVerificationResult verified = await _encryption.VerifyAsync(
                ciphertextPath,
                protectors,
                cancellationToken).ConfigureAwait(false);
            if (!verified.Succeeded || verified.PlaintextBytes != plaintextBytes)
                return VaultAddItemResult.Fail(
                    "CiphertextVerificationFailed:" + verified.Code,
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);

            transaction = transaction with
            {
                State = VaultTransactionState.CiphertextReady,
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            VaultMetadataSnapshot ciphertextReady = NextSnapshot(current, current.Items, new[] { transaction });
            VaultMetadataWriteResult ciphertextWrite = await _metadata.WriteSnapshotAsync(ciphertextReady, cancellationToken).ConfigureAwait(false);
            if (!ciphertextWrite.Succeeded)
                return VaultAddItemResult.Fail(
                    "CiphertextStateCommitFailed:" + ciphertextWrite.Code,
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);
            current = ciphertextReady;

            List<VaultMetadataItem> items = current.Items.ToList();
            items.Add(new VaultMetadataItem(itemId, ciphertextFileName, plaintextBytes, wrappedItemKey));
            transaction = transaction with
            {
                State = VaultTransactionState.MetadataCommitted,
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            VaultMetadataSnapshot metadataCommitted = NextSnapshot(current, items, new[] { transaction });
            VaultMetadataWriteResult metadataWrite = await _metadata.WriteSnapshotAsync(metadataCommitted, cancellationToken).ConfigureAwait(false);
            if (!metadataWrite.Succeeded)
                return VaultAddItemResult.Fail(
                    "ItemMetadataCommitFailed:" + metadataWrite.Code,
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);
            current = metadataCommitted;

            VaultMetadataReadResult committedRead = await _metadata.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!committedRead.Succeeded || committedRead.Snapshot is null ||
                !committedRead.Snapshot.Items.Any(i =>
                    i.ItemId == itemId &&
                    string.Equals(i.CiphertextFileName, ciphertextFileName, StringComparison.Ordinal) &&
                    i.PlaintextBytes == plaintextBytes) ||
                !committedRead.Snapshot.PendingTransactions.Any(t =>
                    t.TransactionId == transactionId &&
                    t.ItemId == itemId &&
                    t.State == VaultTransactionState.MetadataCommitted))
            {
                return VaultAddItemResult.Fail(
                    "CommittedMetadataVerificationFailed",
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);
            }

            VaultFileKeyProtector openingProtector = VaultFileKeyProtector.ForOpening(_vault, itemId);
            FileContainerVerificationResult finalCiphertext = await _encryption.VerifyAsync(
                ciphertextPath,
                new IFileKeyProtector[] { openingProtector },
                cancellationToken).ConfigureAwait(false);
            if (!finalCiphertext.Succeeded || finalCiphertext.PlaintextBytes != plaintextBytes)
                return VaultAddItemResult.Fail(
                    "FinalCiphertextVerificationFailed:" + finalCiphertext.Code,
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);

            VaultMetadataSnapshot complete = NextSnapshot(
                current,
                current.Items,
                Array.Empty<VaultTransactionRecord>());
            VaultMetadataWriteResult completeWrite = await _metadata.WriteSnapshotAsync(complete, cancellationToken).ConfigureAwait(false);
            if (!completeWrite.Succeeded)
                return VaultAddItemResult.Fail(
                    "CompletionCommitFailed:" + completeWrite.Code,
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);

            VaultMetadataReadResult finalRead = await _metadata.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!finalRead.Succeeded || finalRead.Snapshot is null ||
                finalRead.Snapshot.PendingTransactions.Count != 0 ||
                !finalRead.Snapshot.Items.Any(i => i.ItemId == itemId))
            {
                return VaultAddItemResult.Fail(
                    "CompletionVerificationFailed",
                    itemId,
                    transactionId,
                    ciphertextPath,
                    plaintextBytes,
                    recoveryRequired: true);
            }

            return VaultAddItemResult.Success(itemId, transactionId, ciphertextPath, plaintextBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VaultAddItemResult.Fail(
                "Canceled",
                itemId,
                transactionId,
                ciphertextPath,
                plaintextBytes,
                recoveryRequired: true);
        }
        catch (InvalidOperationException)
        {
            return VaultAddItemResult.Fail(
                "VaultStateChanged",
                itemId,
                transactionId,
                ciphertextPath,
                plaintextBytes,
                recoveryRequired: true);
        }
        catch (IOException)
        {
            return VaultAddItemResult.Fail(
                "IoFailure",
                itemId,
                transactionId,
                ciphertextPath,
                plaintextBytes,
                recoveryRequired: true);
        }
        catch (UnauthorizedAccessException)
        {
            return VaultAddItemResult.Fail(
                "AccessDenied",
                itemId,
                transactionId,
                ciphertextPath,
                plaintextBytes,
                recoveryRequired: true);
        }
    }

    private static VaultMetadataSnapshot NextSnapshot(
        VaultMetadataSnapshot current,
        IReadOnlyList<VaultMetadataItem> items,
        IReadOnlyList<VaultTransactionRecord> pending) =>
        new(
            MetadataVersion,
            current.VaultId,
            checked(current.Generation + 1),
            items.ToArray(),
            pending.ToArray());

    private async Task<FileStream?> TryAcquireOperationLeaseAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _operationLeasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
        }
        return null;
    }

    private static bool TryValidateSource(
        string? sourcePath,
        out string source,
        out long plaintextBytes,
        out string error)
    {
        source = string.Empty;
        plaintextBytes = 0;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error = "InvalidSource";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(sourcePath))
            {
                error = "InvalidSource";
                return false;
            }
            source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source))
            {
                error = "SourceNotFound";
                return false;
            }
            FileAttributes attributes = File.GetAttributes(source);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                error = "UnsupportedSource";
                return false;
            }
            plaintextBytes = new FileInfo(source).Length;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "AccessDenied";
            return false;
        }
        catch (IOException)
        {
            error = "IoFailure";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "InvalidSource";
            return false;
        }
    }

    private static void RejectReparseDirectory(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(label + " cannot be a reparse point.");
    }

    private static VaultWrappedItemKey CloneWrappedItemKey(VaultWrappedItemKey record) =>
        new(
            record.Version,
            record.VaultId,
            record.ItemId,
            record.Nonce.ToArray(),
            record.WrappedItemKey.ToArray());
}

internal sealed record VaultAddItemResult(
    bool Succeeded,
    string Code,
    Guid ItemId,
    Guid TransactionId,
    string CiphertextPath,
    long PlaintextBytes,
    bool RecoveryRequired)
{
    internal static VaultAddItemResult Success(
        Guid itemId,
        Guid transactionId,
        string ciphertextPath,
        long plaintextBytes) =>
        new(true, "VerifiedCommitted", itemId, transactionId, ciphertextPath, plaintextBytes, false);

    internal static VaultAddItemResult Fail(
        string code,
        Guid itemId = default,
        Guid transactionId = default,
        string ciphertextPath = "",
        long plaintextBytes = 0,
        bool recoveryRequired = false) =>
        new(false, code, itemId, transactionId, ciphertextPath, plaintextBytes, recoveryRequired);
}
