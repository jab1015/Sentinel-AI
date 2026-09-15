using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

/// <summary>
/// Removes authenticated Vault items only after their plaintext restore has already completed.
/// Metadata is committed first so a crash can never leave the Vault index pointing at deleted
/// ciphertext. Ciphertext cleanup happens only after the authenticated index no longer exposes
/// the restored items.
/// </summary>
internal sealed class VaultRestoredItemRetirementService
{
    private const string ItemsDirectoryName = "items";
    private const string OperationLeaseFileName = ".vault-item-operation.lock";

    private readonly string _root;
    private readonly string _itemsRoot;
    private readonly string _operationLeasePath;
    private readonly SentinelVaultService _vault;
    private readonly VaultMetadataStore _metadata;

    internal VaultRestoredItemRetirementService(string root, SentinelVaultService vault)
    {
        _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
        _itemsRoot = Path.Combine(_root, ItemsDirectoryName);
        _operationLeasePath = Path.Combine(_root, OperationLeaseFileName);
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _metadata = new VaultMetadataStore(_root, _vault);
    }

    internal async Task<VaultRetirementResult> RetireAsync(
        IReadOnlyCollection<Guid> requestedItemIds,
        CancellationToken cancellationToken = default)
    {
        if (_vault.State != VaultState.Unlocked || _vault.VaultId == Guid.Empty)
            return VaultRetirementResult.Fail("VaultLocked");
        if (requestedItemIds is null || requestedItemIds.Count == 0)
            return VaultRetirementResult.Fail("NoItemsSelected");

        Guid[] itemIds = requestedItemIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (itemIds.Length != requestedItemIds.Count)
            return VaultRetirementResult.Fail("InvalidOrDuplicateItemSelection");

        FileStream? operationLease;
        try { operationLease = await TryAcquireOperationLeaseAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return VaultRetirementResult.Fail("Canceled"); }
        catch (UnauthorizedAccessException) { return VaultRetirementResult.Fail("AccessDenied"); }
        catch (IOException) { return VaultRetirementResult.Fail("OperationLeaseUnavailable"); }
        if (operationLease is null) return VaultRetirementResult.Fail("OperationLeaseUnavailable");
        await using FileStream lease = operationLease;

        if (!TryValidateStorageBoundary(out string boundaryError))
            return VaultRetirementResult.Fail(boundaryError);

        VaultMetadataReadResult read = await _metadata.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded || read.Snapshot is null)
            return VaultRetirementResult.Fail("MetadataUnavailable:" + read.Code);

        VaultMetadataSnapshot current = read.Snapshot;
        if (current.VaultId != _vault.VaultId)
            return VaultRetirementResult.Fail("VaultIdentityMismatch");
        if (current.PendingTransactions.Count != 0)
            return VaultRetirementResult.Fail("RecoveryRequired");

        Dictionary<Guid, VaultMetadataItem> selected = current.Items
            .Where(item => itemIds.Contains(item.ItemId))
            .ToDictionary(item => item.ItemId);
        if (selected.Count != itemIds.Length)
            return VaultRetirementResult.Fail("SelectedItemMissing");

        Dictionary<Guid, string> ciphertextPaths = new();
        foreach (Guid itemId in itemIds)
        {
            VaultMetadataItem item = selected[itemId];
            if (!TryGetCiphertextPath(item, out string ciphertextPath))
                return VaultRetirementResult.Fail("CiphertextPathInvalid");
            if (!File.Exists(ciphertextPath) || IsReparsePoint(ciphertextPath))
                return VaultRetirementResult.Fail("CiphertextUnavailable");
            ciphertextPaths[itemId] = ciphertextPath;
        }

        long nextGeneration;
        try { nextGeneration = checked(current.Generation + 1); }
        catch (OverflowException) { return VaultRetirementResult.Fail("GenerationOverflow"); }

        HashSet<Guid> retiring = itemIds.ToHashSet();
        VaultMetadataSnapshot retiredSnapshot = new(
            current.Version,
            current.VaultId,
            nextGeneration,
            current.Items.Where(item => !retiring.Contains(item.ItemId)).ToArray(),
            current.PendingTransactions.ToArray());

        VaultMetadataWriteResult write = await _metadata.WriteSnapshotAsync(retiredSnapshot, cancellationToken).ConfigureAwait(false);
        if (!write.Succeeded)
            return VaultRetirementResult.Fail("MetadataRetirementFailed:" + write.Code);

        VaultMetadataReadResult verification = await _metadata.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!verification.Succeeded || verification.Snapshot is null ||
            verification.Snapshot.Items.Any(item => retiring.Contains(item.ItemId)))
            return VaultRetirementResult.Fail("MetadataRetirementVerificationFailed", metadataRetired: true);

        int deleted = 0;
        foreach (string ciphertextPath in ciphertextPaths.Values)
        {
            try
            {
                if (!TryValidateStorageBoundary(out boundaryError))
                    return VaultRetirementResult.Partial(itemIds.Length, deleted, boundaryError);
                File.Delete(ciphertextPath);
                if (File.Exists(ciphertextPath))
                    return VaultRetirementResult.Partial(itemIds.Length, deleted, "CiphertextCleanupNotVerified");
                deleted++;
            }
            catch (UnauthorizedAccessException) { return VaultRetirementResult.Partial(itemIds.Length, deleted, "CiphertextCleanupAccessDenied"); }
            catch (IOException) { return VaultRetirementResult.Partial(itemIds.Length, deleted, "CiphertextCleanupIoFailure"); }
        }

        return VaultRetirementResult.Success(itemIds.Length);
    }

    private async Task<FileStream?> TryAcquireOperationLeaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return new FileStream(_operationLeasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
        return null;
    }

    private bool TryValidateStorageBoundary(out string error)
    {
        error = string.Empty;
        try
        {
            if (!Directory.Exists(_root) || !Directory.Exists(_itemsRoot)) { error = "StorageBoundaryMissing"; return false; }
            string root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string items = Path.GetFullPath(_itemsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string expected = Path.Combine(root, ItemsDirectoryName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(items, expected, StringComparison.OrdinalIgnoreCase)) { error = "StorageBoundaryChanged"; return false; }
            if (IsReparsePoint(root) || IsReparsePoint(items)) { error = "StorageBoundaryReparsePoint"; return false; }
            return true;
        }
        catch (UnauthorizedAccessException) { error = "StorageBoundaryAccessDenied"; return false; }
        catch (IOException) { error = "StorageBoundaryIoFailure"; return false; }
    }

    private bool TryGetCiphertextPath(VaultMetadataItem item, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(item.CiphertextFileName) ||
            item.CiphertextFileName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            return false;
        string candidate = Path.GetFullPath(Path.Combine(_itemsRoot, item.CiphertextFileName));
        string prefix = Path.TrimEndingDirectorySeparator(_itemsRoot) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        path = candidate;
        return true;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }
}

internal sealed record VaultRetirementResult(
    bool Succeeded,
    bool MetadataRetired,
    bool CleanupComplete,
    string Code,
    int Requested,
    int CiphertextsDeleted)
{
    internal static VaultRetirementResult Success(int count) => new(true, true, true, "VerifiedRetired", count, count);
    internal static VaultRetirementResult Partial(int requested, int deleted, string code) => new(false, true, false, code, requested, deleted);
    internal static VaultRetirementResult Fail(string code, bool metadataRetired = false) => new(false, metadataRetired, false, code, 0, 0);
}
