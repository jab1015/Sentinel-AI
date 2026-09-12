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

        FileStream? operationLease;
        try
        {
            operationLease = await TryAcquireOperationLeaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VaultAddItemResult.Fail("Canceled");
        }
        await using (operationLease)
        {
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

            try
            {
                using VaultItemKeyLease itemKey = _vault.CreateItemKey(itemId);
                VaultWrappedItemKey wrappedItemKey = CloneWrappedItemKey(itemKey.WrappedItemKey);
                using VaultFileKeyProtector protector = VaultFileKeyProtector.ForEncryption(_vault, itemKey);
                IReadOnlyList<IFileKeyProtector> protectors = new IFileKeyProtector[] { protector };

                List<VaultMetadataItem> items = current.Items.ToList();
                items.Add(new VaultMetadataItem(itemId, ciphertextFileName, plaintextBytes, wrappedItemKey));

                VaultTransactionRecord transaction = new(
                    transactionId,
                    itemId,
                    VaultTransactionState.Prepared,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                VaultMetadataSnapshot prepared = NextSnapshot(current, items, new[] { transaction });
                VaultMetadataWriteResult preparedWrite = await _metadata.WriteSnapshotAsync(prepared, cancellationToken).ConfigureAwait(false);
                if (!preparedWrite.Succeeded)
                    return VaultAddItemResult.Fail(
                        "PrepareMetadataFailed:" + preparedWrite.Code,
                        itemId,
                        transactionId,
                        ciphertextPath,
                        plaintextBytes);
                current = prepared;

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

                transaction = transaction with
                {
                    State = VaultTransactionState.MetadataCommitted,
                    UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                VaultMetadataSnapshot metadataCommitted = NextSnapshot(current, current.Items, new[] { transaction });
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

                VaultRecoveryCheckResult committedCheck = await VerifyPendingItemAsync(
                    current,
                    transaction,
                    cancellationToken).ConfigureAwait(false);
                if (!committedCheck.Succeeded)
                    return VaultAddItemResult.Fail(
                        "CommittedVerificationFailed:" + committedCheck.Code,
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
            catch (OverflowException)
            {
                return VaultAddItemResult.Fail(
                    "GenerationOverflow",
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
        }
    }

    internal async Task<VaultRecoveryResult> RecoverPendingAsync(
        CancellationToken cancellationToken = default)
    {
        if (_vault.State != VaultState.Unlocked || _vault.VaultId == Guid.Empty)
            return VaultRecoveryResult.Fail("VaultLocked");

        FileStream? operationLease;
        try
        {
            operationLease = await TryAcquireOperationLeaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VaultRecoveryResult.Fail("Canceled");
        }

        await using (operationLease)
        {
            if (operationLease is null)
                return VaultRecoveryResult.Fail("OperationLeaseUnavailable");

            VaultMetadataReadResult read = await _metadata.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (read.Code == "NotFound") return VaultRecoveryResult.Success(0);
            if (!read.Succeeded || read.Snapshot is null)
                return VaultRecoveryResult.Fail("MetadataUnavailable:" + read.Code);

            VaultMetadataSnapshot current = read.Snapshot;
            if (current.VaultId != _vault.VaultId)
                return VaultRecoveryResult.Fail("VaultIdentityMismatch");

            int recovered = 0;
            while (current.PendingTransactions.Count > 0)
            {
                if (current.PendingTransactions.Count != 1)
                    return VaultRecoveryResult.Fail("AmbiguousPendingTransactions", recovered);

                VaultTransactionRecord transaction = current.PendingTransactions[0];
                VaultMetadataItem? item = current.Items.SingleOrDefault(i => i.ItemId == transaction.ItemId);
                if (item is null)
                    return VaultRecoveryResult.Fail("PendingItemMetadataMissing", recovered);

                if (!TryGetCiphertextPath(item, out string ciphertextPath))
                    return VaultRecoveryResult.Fail("PendingCiphertextPathInvalid", recovered);

                if (transaction.State == VaultTransactionState.Prepared && !File.Exists(ciphertextPath))
                {
                    VaultMetadataSnapshot rolledBack = NextSnapshot(
                        current,
                        current.Items.Where(i => i.ItemId != item.ItemId).ToArray(),
                        Array.Empty<VaultTransactionRecord>());
                    VaultMetadataWriteResult rollbackWrite = await _metadata.WriteSnapshotAsync(rolledBack, cancellationToken).ConfigureAwait(false);
                    if (!rollbackWrite.Succeeded)
                        return VaultRecoveryResult.Fail("PreparedRollbackCommitFailed:" + rollbackWrite.Code, recovered);
                    current = rolledBack;
                    recovered++;
                    continue;
                }

                VaultRecoveryCheckResult check = await VerifyPendingItemAsync(
                    current,
                    transaction,
                    cancellationToken).ConfigureAwait(false);
                if (!check.Succeeded)
                    return VaultRecoveryResult.Fail("PendingCiphertextRejected:" + check.Code, recovered);

                if (transaction.State == VaultTransactionState.Prepared)
                {
                    transaction = transaction with
                    {
                        State = VaultTransactionState.CiphertextReady,
                        UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    VaultMetadataSnapshot ready = NextSnapshot(current, current.Items, new[] { transaction });
                    VaultMetadataWriteResult readyWrite = await _metadata.WriteSnapshotAsync(ready, cancellationToken).ConfigureAwait(false);
                    if (!readyWrite.Succeeded)
                        return VaultRecoveryResult.Fail("RecoveryCiphertextStateCommitFailed:" + readyWrite.Code, recovered);
                    current = ready;
                }

                if (transaction.State == VaultTransactionState.CiphertextReady)
                {
                    transaction = transaction with
                    {
                        State = VaultTransactionState.MetadataCommitted,
                        UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    VaultMetadataSnapshot committed = NextSnapshot(current, current.Items, new[] { transaction });
                    VaultMetadataWriteResult committedWrite = await _metadata.WriteSnapshotAsync(committed, cancellationToken).ConfigureAwait(false);
                    if (!committedWrite.Succeeded)
                        return VaultRecoveryResult.Fail("RecoveryMetadataCommitFailed:" + committedWrite.Code, recovered);
                    current = committed;
                }

                if (transaction.State is VaultTransactionState.MetadataCommitted or VaultTransactionState.Complete)
                {
                    VaultRecoveryCheckResult finalCheck = await VerifyPendingItemAsync(
                        current,
                        transaction,
                        cancellationToken).ConfigureAwait(false);
                    if (!finalCheck.Succeeded)
                        return VaultRecoveryResult.Fail("RecoveryFinalVerificationFailed:" + finalCheck.Code, recovered);

                    VaultMetadataSnapshot complete = NextSnapshot(
                        current,
                        current.Items,
                        Array.Empty<VaultTransactionRecord>());
                    VaultMetadataWriteResult completeWrite = await _metadata.WriteSnapshotAsync(complete, cancellationToken).ConfigureAwait(false);
                    if (!completeWrite.Succeeded)
                        return VaultRecoveryResult.Fail("RecoveryCompletionCommitFailed:" + completeWrite.Code, recovered);
                    current = complete;
                    recovered++;
                    continue;
                }

                if (transaction.State == VaultTransactionState.CleanupPending)
                    return VaultRecoveryResult.Fail("CleanupRecoveryNotImplemented", recovered);

                return VaultRecoveryResult.Fail("UnsupportedTransactionState", recovered);
            }

            return VaultRecoveryResult.Success(recovered);
        }
    }

    private async Task<VaultRecoveryCheckResult> VerifyPendingItemAsync(
        VaultMetadataSnapshot snapshot,
        VaultTransactionRecord transaction,
        CancellationToken cancellationToken)
    {
        VaultMetadataItem? item = snapshot.Items.SingleOrDefault(i => i.ItemId == transaction.ItemId);
        if (item is null) return VaultRecoveryCheckResult.Fail("ItemMetadataMissing");
        if (item.WrappedItemKey.VaultId != snapshot.VaultId || item.WrappedItemKey.ItemId != item.ItemId)
            return VaultRecoveryCheckResult.Fail("WrappedItemKeyMismatch");
        if (!TryGetCiphertextPath(item, out string path) || !File.Exists(path))
            return VaultRecoveryCheckResult.Fail("CiphertextMissing");

        VaultFileKeyProtector openingProtector = VaultFileKeyProtector.ForOpening(_vault, item.ItemId);
        FileContainerVerificationResult verification = await _encryption.VerifyAsync(
            path,
            new IFileKeyProtector[] { openingProtector },
            cancellationToken).ConfigureAwait(false);
        if (!verification.Succeeded)
            return VaultRecoveryCheckResult.Fail(verification.Code);
        if (verification.PlaintextBytes != item.PlaintextBytes)
            return VaultRecoveryCheckResult.Fail("PlaintextLengthMismatch");
        return VaultRecoveryCheckResult.Success();
    }

    private bool TryGetCiphertextPath(VaultMetadataItem item, out string path)
    {
        path = string.Empty;
        try
        {
            string candidate = Path.GetFullPath(Path.Combine(_itemsRoot, item.CiphertextFileName));
            string prefix = _itemsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(Path.GetFileName(candidate), item.CiphertextFileName, StringComparison.Ordinal)) return false;
            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
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

internal sealed record VaultRecoveryResult(bool Succeeded, string Code, int RecoveredTransactions)
{
    internal static VaultRecoveryResult Success(int recovered) => new(true, "VerifiedRecovered", recovered);
    internal static VaultRecoveryResult Fail(string code, int recovered = 0) => new(false, code, recovered);
}

internal sealed record VaultRecoveryCheckResult(bool Succeeded, string Code)
{
    internal static VaultRecoveryCheckResult Success() => new(true, "Verified");
    internal static VaultRecoveryCheckResult Fail(string code) => new(false, code);
}
