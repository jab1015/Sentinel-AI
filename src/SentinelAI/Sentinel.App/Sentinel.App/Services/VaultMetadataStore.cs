using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class VaultMetadataStore
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SNTLVMD1");
    private static readonly byte[] PayloadContext = Encoding.ASCII.GetBytes("SentinelAI.VaultMetadata.v1");
    private const int FormatVersion = 1;
    private const int HeaderLength = 8 + 4 + 16 + 16 + 12 + 48 + 12 + 4;
    private const int TagLength = 16;
    private const int MaximumPlaintextBytes = 4 * 1024 * 1024;
    private const int MaximumContainerBytes = MaximumPlaintextBytes + HeaderLength + TagLength;
    private const string MetadataFileName = "vault-metadata.svmd";
    private const string LeaseFileName = ".vault-writer.lock";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    private readonly string _root;
    private readonly string _metadataPath;
    private readonly string _leasePath;
    private readonly SentinelVaultService _vault;

    internal VaultMetadataStore(string root, SentinelVaultService vault)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A vault metadata root is required.", nameof(root));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        if ((File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Vault metadata root cannot be a reparse point.");
        _metadataPath = Path.Combine(_root, MetadataFileName);
        _leasePath = Path.Combine(_root, LeaseFileName);
    }

    internal async Task<VaultMetadataWriteResult> WriteSnapshotAsync(
        VaultMetadataSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        if (_vault.State != VaultState.Unlocked || _vault.VaultId != snapshot.VaultId)
            return VaultMetadataWriteResult.Fail("VaultLockedOrMismatched");

        FileStream lease;
        try
        {
            lease = await AcquireWriterLeaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VaultMetadataWriteResult.Fail("Canceled");
        }
        catch (UnauthorizedAccessException)
        {
            return VaultMetadataWriteResult.Fail("AccessDenied");
        }
        catch (IOException)
        {
            return VaultMetadataWriteResult.Fail("IoFailure");
        }

        await using FileStream writerLease = lease;
        string tempPath = Path.Combine(_root, $".{MetadataFileName}.{Guid.NewGuid():N}.tmp");
        bool tempCreated = false;
        try
        {
            VaultMetadataReadResult current = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (current.Succeeded && current.Snapshot is not null)
            {
                if (snapshot.Generation != checked(current.Snapshot.Generation + 1))
                    return VaultMetadataWriteResult.Fail("GenerationConflict");
            }
            else if (current.Code == "NotFound")
            {
                if (snapshot.Generation != 0)
                    return VaultMetadataWriteResult.Fail("GenerationConflict");
            }
            else
            {
                return VaultMetadataWriteResult.Fail("CurrentSnapshotUnavailable:" + current.Code);
            }

            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            if (plaintext.Length is <= 0 or > MaximumPlaintextBytes)
                return VaultMetadataWriteResult.Fail("SnapshotTooLarge");

            Guid metadataItemId = Guid.NewGuid();
            using VaultItemKeyLease keyLease = _vault.CreateItemKey(metadataItemId);
            VaultWrappedItemKey wrappedKey = keyLease.WrappedItemKey;
            if (wrappedKey.Nonce.Length != 12 || wrappedKey.WrappedItemKey.Length != 48)
                return VaultMetadataWriteResult.Fail("WrappedMetadataKeyInvalid");

            byte[] payloadNonce = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagLength];
            byte[] header = BuildHeader(snapshot.VaultId, wrappedKey, payloadNonce, plaintext.Length);
            byte[] aad = BuildPayloadAad(header);
            try
            {
                using AesGcm aes = new(keyLease.ItemKey.Span, TagLength);
                aes.Encrypt(payloadNonce, plaintext, ciphertext, tag, aad);

                await using FileStream output = new(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                tempCreated = true;
                await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(payloadNonce);
                CryptographicOperations.ZeroMemory(aad);
            }

            VaultMetadataReadResult verification = await ReadSnapshotFromPathAsync(tempPath, cancellationToken).ConfigureAwait(false);
            if (!verification.Succeeded || verification.Snapshot is null || !SnapshotsEquivalent(verification.Snapshot, snapshot))
                return VaultMetadataWriteResult.Fail("TempVerificationFailed");

            File.Move(tempPath, _metadataPath, overwrite: true);
            tempCreated = false;

            VaultMetadataReadResult committed = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!committed.Succeeded || committed.Snapshot is null || !SnapshotsEquivalent(committed.Snapshot, snapshot))
                return VaultMetadataWriteResult.Fail("CommittedVerificationFailed");

            return VaultMetadataWriteResult.Success(snapshot.Generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VaultMetadataWriteResult.Fail("Canceled");
        }
        catch (OverflowException)
        {
            return VaultMetadataWriteResult.Fail("GenerationOverflow");
        }
        catch (UnauthorizedAccessException)
        {
            return VaultMetadataWriteResult.Fail("AccessDenied");
        }
        catch (IOException)
        {
            return VaultMetadataWriteResult.Fail("IoFailure");
        }
        catch (CryptographicException)
        {
            return VaultMetadataWriteResult.Fail("CryptographicFailure");
        }
        catch (JsonException)
        {
            return VaultMetadataWriteResult.Fail("SerializationFailure");
        }
        finally
        {
            if (tempCreated)
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    internal Task<VaultMetadataReadResult> ReadSnapshotAsync(CancellationToken cancellationToken = default) =>
        ReadSnapshotFromPathAsync(_metadataPath, cancellationToken);

    private async Task<VaultMetadataReadResult> ReadSnapshotFromPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (_vault.State != VaultState.Unlocked)
            return VaultMetadataReadResult.Fail("VaultLocked");

        try
        {
            await using FileStream input = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length is < HeaderLength + TagLength or > MaximumContainerBytes)
                return VaultMetadataReadResult.Fail("InvalidLength");

            byte[] header = new byte[HeaderLength];
            await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                return VaultMetadataReadResult.Fail("BadMagic");
            if (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4)) != FormatVersion)
                return VaultMetadataReadResult.Fail("UnsupportedVersion");

            Guid vaultId = new(header.AsSpan(12, 16));
            Guid itemId = new(header.AsSpan(28, 16));
            byte[] keyNonce = header.AsSpan(44, 12).ToArray();
            byte[] wrapped = header.AsSpan(56, 48).ToArray();
            byte[] payloadNonce = header.AsSpan(104, 12).ToArray();
            int ciphertextLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(116, 4));
            if (vaultId == Guid.Empty || itemId == Guid.Empty || vaultId != _vault.VaultId ||
                ciphertextLength is <= 0 or > MaximumPlaintextBytes ||
                input.Length != HeaderLength + (long)ciphertextLength + TagLength)
                return VaultMetadataReadResult.Fail("InvalidHeader");

            VaultWrappedItemKey wrappedMetadataKey = new(1, vaultId, itemId, keyNonce, wrapped);
            using VaultItemKeyLease? keyLease = _vault.TryOpenItemKey(wrappedMetadataKey);
            if (keyLease is null) return VaultMetadataReadResult.Fail("MetadataKeyRejected");

            byte[] ciphertext = new byte[ciphertextLength];
            byte[] tag = new byte[TagLength];
            byte[] plaintext = new byte[ciphertextLength];
            byte[] aad = BuildPayloadAad(header);
            try
            {
                await input.ReadExactlyAsync(ciphertext, cancellationToken).ConfigureAwait(false);
                await input.ReadExactlyAsync(tag, cancellationToken).ConfigureAwait(false);
                if (input.Position != input.Length) return VaultMetadataReadResult.Fail("TrailingData");

                using AesGcm aes = new(keyLease.ItemKey.Span, TagLength);
                aes.Decrypt(payloadNonce, ciphertext, tag, plaintext, aad);
                VaultMetadataSnapshot? snapshot = JsonSerializer.Deserialize<VaultMetadataSnapshot>(plaintext, JsonOptions);
                if (snapshot is null) return VaultMetadataReadResult.Fail("InvalidPayload");
                ValidateSnapshot(snapshot);
                if (snapshot.VaultId != vaultId) return VaultMetadataReadResult.Fail("VaultIdMismatch");
                return VaultMetadataReadResult.Success(snapshot);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyNonce);
                CryptographicOperations.ZeroMemory(wrapped);
                CryptographicOperations.ZeroMemory(payloadNonce);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(aad);
            }
        }
        catch (FileNotFoundException)
        {
            return VaultMetadataReadResult.Fail("NotFound");
        }
        catch (DirectoryNotFoundException)
        {
            return VaultMetadataReadResult.Fail("NotFound");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VaultMetadataReadResult.Fail("Canceled");
        }
        catch (CryptographicException)
        {
            return VaultMetadataReadResult.Fail("AuthenticationFailed");
        }
        catch (JsonException)
        {
            return VaultMetadataReadResult.Fail("InvalidPayload");
        }
        catch (InvalidDataException)
        {
            return VaultMetadataReadResult.Fail("InvalidPayload");
        }
        catch (IOException)
        {
            return VaultMetadataReadResult.Fail("IoFailure");
        }
        catch (UnauthorizedAccessException)
        {
            return VaultMetadataReadResult.Fail("AccessDenied");
        }
    }

    private async Task<FileStream> AcquireWriterLeaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return new FileStream(_leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException("Vault metadata writer lease is unavailable.");
    }

    private static byte[] BuildHeader(Guid vaultId, VaultWrappedItemKey wrappedKey, byte[] payloadNonce, int plaintextLength)
    {
        byte[] header = new byte[HeaderLength];
        int offset = 0;
        Magic.CopyTo(header, offset); offset += Magic.Length;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset, 4), FormatVersion); offset += 4;
        vaultId.ToByteArray().CopyTo(header, offset); offset += 16;
        wrappedKey.ItemId.ToByteArray().CopyTo(header, offset); offset += 16;
        wrappedKey.Nonce.CopyTo(header, offset); offset += 12;
        wrappedKey.WrappedItemKey.CopyTo(header, offset); offset += 48;
        payloadNonce.CopyTo(header, offset); offset += 12;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset, 4), plaintextLength);
        return header;
    }

    private static byte[] BuildPayloadAad(byte[] header)
    {
        byte[] aad = new byte[PayloadContext.Length + header.Length];
        PayloadContext.CopyTo(aad, 0);
        header.CopyTo(aad, PayloadContext.Length);
        return aad;
    }

    private static bool SnapshotsEquivalent(VaultMetadataSnapshot left, VaultMetadataSnapshot right)
    {
        byte[] leftBytes = JsonSerializer.SerializeToUtf8Bytes(left, JsonOptions);
        byte[] rightBytes = JsonSerializer.SerializeToUtf8Bytes(right, JsonOptions);
        try
        {
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static void ValidateSnapshot(VaultMetadataSnapshot snapshot)
    {
        if (snapshot.Version != FormatVersion || snapshot.VaultId == Guid.Empty || snapshot.Generation < 0)
            throw new InvalidDataException("Vault metadata snapshot identity/version is invalid.");
        if (snapshot.Items is null || snapshot.PendingTransactions is null)
            throw new InvalidDataException("Vault metadata snapshot collections are required.");
        if (snapshot.Items.Count > 100_000 || snapshot.PendingTransactions.Count > 10_000)
            throw new InvalidDataException("Vault metadata snapshot exceeded supported collection bounds.");

        HashSet<Guid> itemIds = new();
        foreach (VaultMetadataItem item in snapshot.Items)
        {
            if (item.ItemId == Guid.Empty || !itemIds.Add(item.ItemId) ||
                string.IsNullOrWhiteSpace(item.CiphertextFileName) || item.CiphertextFileName.Length > 255 ||
                item.CiphertextFileName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0 ||
                item.WrappedItemKey is null || item.WrappedItemKey.VaultId != snapshot.VaultId || item.WrappedItemKey.ItemId != item.ItemId)
                throw new InvalidDataException("Vault metadata item is invalid.");
        }

        HashSet<Guid> transactionIds = new();
        foreach (VaultTransactionRecord transaction in snapshot.PendingTransactions)
        {
            if (transaction.TransactionId == Guid.Empty || !transactionIds.Add(transaction.TransactionId) ||
                transaction.ItemId == Guid.Empty || !Enum.IsDefined(transaction.State))
                throw new InvalidDataException("Vault transaction record is invalid.");
        }
    }
}

internal sealed record VaultMetadataSnapshot(
    int Version,
    Guid VaultId,
    long Generation,
    IReadOnlyList<VaultMetadataItem> Items,
    IReadOnlyList<VaultTransactionRecord> PendingTransactions);

internal sealed record VaultMetadataItem(
    Guid ItemId,
    string CiphertextFileName,
    long PlaintextBytes,
    VaultWrappedItemKey WrappedItemKey);

internal enum VaultTransactionState
{
    Prepared,
    CiphertextReady,
    MetadataCommitted,
    CleanupPending,
    Complete
}

internal sealed record VaultTransactionRecord(
    Guid TransactionId,
    Guid ItemId,
    VaultTransactionState State,
    long UpdatedUnixMs);

internal sealed record VaultMetadataWriteResult(bool Succeeded, string Code, long Generation)
{
    internal static VaultMetadataWriteResult Success(long generation) => new(true, "VerifiedCommitted", generation);
    internal static VaultMetadataWriteResult Fail(string code) => new(false, code, -1);
}

internal sealed record VaultMetadataReadResult(bool Succeeded, string Code, VaultMetadataSnapshot? Snapshot)
{
    internal static VaultMetadataReadResult Success(VaultMetadataSnapshot snapshot) => new(true, "Verified", snapshot);
    internal static VaultMetadataReadResult Fail(string code) => new(false, code, null);
}
