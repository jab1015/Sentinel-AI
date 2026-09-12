using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class VaultItemExportService
{
    private const string ItemsDirectoryName = "items";
    private const string OperationLeaseFileName = ".vault-item-operation.lock";

    private readonly string _root;
    private readonly string _itemsRoot;
    private readonly string _operationLeasePath;
    private readonly SentinelVaultService _vault;
    private readonly VaultItemStoreService _itemStore;
    private readonly FileEncryptionService _encryption;

    internal VaultItemExportService(
        string root,
        SentinelVaultService vault,
        VaultItemStoreService itemStore,
        FileEncryptionService? encryption = null)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("A Vault storage root is required.", nameof(root));

        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _itemStore = itemStore ?? throw new ArgumentNullException(nameof(itemStore));
        _encryption = encryption ?? new FileEncryptionService();
        _root = Path.GetFullPath(root);
        _itemsRoot = Path.Combine(_root, ItemsDirectoryName);
        _operationLeasePath = Path.Combine(_root, OperationLeaseFileName);
    }

    internal async Task<VaultExportResult> ExportAsync(
        Guid itemId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (itemId == Guid.Empty)
            return VaultExportResult.Fail("InvalidItemId");
        if (_vault.State != VaultState.Unlocked || _vault.VaultId == Guid.Empty)
            return VaultExportResult.Fail("VaultLocked", itemId);
        if (!TryValidateDestination(destinationPath, out string destination, out string destinationError))
            return VaultExportResult.Fail(destinationError, itemId, destinationPath ?? string.Empty);

        FileStream? operationLease;
        try
        {
            operationLease = await TryAcquireOperationLeaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VaultExportResult.Fail("Canceled", itemId, destination);
        }

        if (operationLease is null)
            return VaultExportResult.Fail("OperationLeaseUnavailable", itemId, destination);
        await using FileStream lease = operationLease;

        if (!TryValidateStorageBoundary(out string boundaryError))
            return VaultExportResult.Fail(boundaryError, itemId, destination);
        if (!TryValidateDestination(destination, out destination, out destinationError))
            return VaultExportResult.Fail(destinationError, itemId, destination);
        if (_vault.State != VaultState.Unlocked || _vault.VaultId == Guid.Empty)
            return VaultExportResult.Fail("VaultLocked", itemId, destination);

        VaultCommittedItemsResult committed = await _itemStore.ListCommittedItemsAsync(cancellationToken).ConfigureAwait(false);
        if (!committed.Succeeded)
            return VaultExportResult.Fail("CommittedItemsUnavailable:" + committed.Code, itemId, destination);

        VaultMetadataItem[] matches = committed.Items.Where(i => i.ItemId == itemId).ToArray();
        if (matches.Length == 0)
            return VaultExportResult.Fail("ItemNotCommitted", itemId, destination);
        if (matches.Length != 1)
            return VaultExportResult.Fail("AmbiguousItemIdentity", itemId, destination);
        VaultMetadataItem item = matches[0];

        if (!TryGetCiphertextPath(item, out string ciphertextPath))
            return VaultExportResult.Fail("CiphertextPathInvalid", itemId, destination);
        if (!File.Exists(ciphertextPath))
            return VaultExportResult.Fail("CiphertextMissing", itemId, destination);
        if (IsReparsePoint(ciphertextPath))
            return VaultExportResult.Fail("CiphertextReparsePoint", itemId, destination);

        VaultFileKeyProtector openingProtector = VaultFileKeyProtector.ForOpening(_vault, itemId);
        FileContainerVerificationResult before = await _encryption.VerifyAsync(
            ciphertextPath,
            new IFileKeyProtector[] { openingProtector },
            cancellationToken).ConfigureAwait(false);
        if (!before.Succeeded || before.PlaintextBytes != item.PlaintextBytes)
            return VaultExportResult.Fail("CiphertextVerificationFailed:" + before.Code, itemId, destination);

        FileDecryptionResult decrypted = await _encryption.DecryptAsync(
            ciphertextPath,
            destination,
            new IFileKeyProtector[] { openingProtector },
            cancellationToken).ConfigureAwait(false);
        if (!decrypted.Succeeded)
            return VaultExportResult.Fail(
                "ExportFailed:" + decrypted.Code,
                itemId,
                destination,
                decrypted.OutputRemains);

        try
        {
            if (!File.Exists(destination) || IsReparsePoint(destination))
                return VaultExportResult.Fail("ExportVerificationFailed", itemId, destination, outputRemains: File.Exists(destination));
            long actualLength = new FileInfo(destination).Length;
            if (actualLength != item.PlaintextBytes)
                return VaultExportResult.Fail("ExportLengthMismatch", itemId, destination, outputRemains: true);
        }
        catch (UnauthorizedAccessException)
        {
            return VaultExportResult.Fail("ExportVerificationAccessDenied", itemId, destination, outputRemains: File.Exists(destination));
        }
        catch (IOException)
        {
            return VaultExportResult.Fail("ExportVerificationIoFailure", itemId, destination, outputRemains: File.Exists(destination));
        }

        if (!TryValidateStorageBoundary(out boundaryError))
            return VaultExportResult.Fail(boundaryError, itemId, destination, outputRemains: true);

        FileContainerVerificationResult after = await _encryption.VerifyAsync(
            ciphertextPath,
            new IFileKeyProtector[] { openingProtector },
            cancellationToken).ConfigureAwait(false);
        if (!after.Succeeded || after.PlaintextBytes != item.PlaintextBytes)
            return VaultExportResult.Fail("PostExportCiphertextVerificationFailed:" + after.Code, itemId, destination, outputRemains: true);

        return VaultExportResult.Success(itemId, destination, item.PlaintextBytes);
    }

    private bool TryValidateDestination(string? destinationPath, out string destination, out string error)
    {
        destination = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            error = "InvalidDestination";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(destinationPath))
            {
                error = "InvalidDestination";
                return false;
            }

            destination = Path.GetFullPath(destinationPath);
            if (IsWithinDirectory(destination, _root))
            {
                error = "DestinationInsideVault";
                return false;
            }
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                error = "DestinationCollision";
                return false;
            }

            string? parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                error = "DestinationDirectoryUnavailable";
                return false;
            }
            if (IsReparsePoint(parent))
            {
                error = "DestinationDirectoryReparsePoint";
                return false;
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "DestinationAccessDenied";
            return false;
        }
        catch (IOException)
        {
            error = "DestinationIoFailure";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "InvalidDestination";
            return false;
        }
    }

    private bool TryValidateStorageBoundary(out string error)
    {
        error = string.Empty;
        try
        {
            if (!Directory.Exists(_root) || !Directory.Exists(_itemsRoot))
            {
                error = "StorageBoundaryMissing";
                return false;
            }
            string canonicalRoot = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string canonicalItems = Path.GetFullPath(_itemsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string expectedItems = Path.Combine(canonicalRoot, ItemsDirectoryName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(canonicalItems, expectedItems, StringComparison.OrdinalIgnoreCase))
            {
                error = "StorageBoundaryChanged";
                return false;
            }
            if (IsReparsePoint(canonicalRoot) || IsReparsePoint(canonicalItems))
            {
                error = "StorageBoundaryReparsePoint";
                return false;
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "StorageBoundaryAccessDenied";
            return false;
        }
        catch (IOException)
        {
            error = "StorageBoundaryIoFailure";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "StorageBoundaryInvalid";
            return false;
        }
    }

    private bool TryGetCiphertextPath(VaultMetadataItem item, out string path)
    {
        path = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(item.CiphertextFileName) ||
                !string.Equals(Path.GetFileName(item.CiphertextFileName), item.CiphertextFileName, StringComparison.Ordinal))
                return false;
            string candidate = Path.GetFullPath(Path.Combine(_itemsRoot, item.CiphertextFileName));
            if (!IsWithinDirectory(candidate, _itemsRoot)) return false;
            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

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

    private static bool IsWithinDirectory(string fullPath, string directory)
    {
        string normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

internal sealed record VaultExportResult(
    bool Succeeded,
    string Code,
    Guid ItemId,
    string DestinationPath,
    long PlaintextBytes,
    bool OutputRemains)
{
    internal static VaultExportResult Success(Guid itemId, string destinationPath, long plaintextBytes) =>
        new(true, "VerifiedExported", itemId, destinationPath, plaintextBytes, true);

    internal static VaultExportResult Fail(
        string code,
        Guid itemId = default,
        string destinationPath = "",
        bool outputRemains = false) =>
        new(false, code, itemId, destinationPath, 0, outputRemains);
}
