using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class SentinelVaultService : IDisposable
{
    private const int VaultFormatVersion = 1;
    private const int MasterKeySize = 32;
    private const int ItemKeySize = 32;
    private const int ItemNonceSize = 12;
    private const int ItemTagSize = 16;
    private const int MaximumKeyRecords = 3;
    private const int MaximumRecordSection = 64 * 1024;
    private static readonly byte[] EnvelopeKeyContext = Encoding.ASCII.GetBytes("SentinelAI.VaultEnvelopeKey.v1");
    private static readonly byte[] EnvelopeAuthContext = Encoding.ASCII.GetBytes("SentinelAI.VaultEnvelopeAuth.v1");
    private static readonly byte[] ItemWrapContext = Encoding.ASCII.GetBytes("SentinelAI.VaultItemDEK.v1");

    private readonly object _gate = new();
    private byte[]? _vaultMasterKey;
    private Guid _vaultId;
    private DateTimeOffset _lastActivityUtc;
    private VaultState _state = VaultState.Locked;
    private long _lockEpoch;
    private CancellationTokenSource? _operationEpochCancellation;
    private bool _disposed;

    internal VaultState State
    {
        get { lock (_gate) return _state; }
    }

    internal Guid VaultId
    {
        get { lock (_gate) return _vaultId; }
    }

    internal async Task<VaultMasterKeyEnvelope> InitializeNewVaultAsync(
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateMasterKeyProtectors(keyProtectors);

        long unlockEpoch;
        lock (_gate)
        {
            if (_state != VaultState.Locked || _vaultMasterKey is not null)
                throw new InvalidOperationException("A vault session is already initialized or unlocked.");
            unlockEpoch = _lockEpoch;
            _state = VaultState.Unlocking;
        }

        byte[] vmk = RandomNumberGenerator.GetBytes(MasterKeySize);
        Guid vaultId = Guid.NewGuid();
        try
        {
            List<WrappedFileKeyRecord> wrapped = new(keyProtectors.Count);
            foreach (IFileKeyProtector protector in keyProtectors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WrappedFileKeyRecord record = await protector.WrapAsync(vmk, cancellationToken).ConfigureAwait(false);
                ValidateMasterKeyRecord(record, protector.ProtectionModeId);
                wrapped.Add(CloneRecord(record));
            }

            byte[] body = SerializeEnvelopeBody(vaultId, wrapped);
            byte[] tag = ComputeEnvelopeTag(vmk, body);
            CryptographicOperations.ZeroMemory(body);
            VaultMasterKeyEnvelope envelope = new(VaultFormatVersion, vaultId, wrapped, tag);

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_state != VaultState.Unlocking || _lockEpoch != unlockEpoch)
                    throw new OperationCanceledException("Vault initialization was invalidated by a lock transition.");
                _operationEpochCancellation?.Dispose();
                _operationEpochCancellation = new CancellationTokenSource();
                _vaultMasterKey = vmk;
                vmk = Array.Empty<byte>();
                _vaultId = vaultId;
                _lastActivityUtc = DateTimeOffset.UtcNow;
                _state = VaultState.Unlocked;
            }

            return envelope;
        }
        catch
        {
            lock (_gate)
            {
                if (_state == VaultState.Unlocking && _lockEpoch == unlockEpoch)
                    _state = VaultState.Locked;
            }
            throw;
        }
        finally
        {
            if (vmk.Length > 0) CryptographicOperations.ZeroMemory(vmk);
        }
    }

    internal async Task<VaultUnlockResult> UnlockAsync(
        VaultMasterKeyEnvelope envelope,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelopeShape(envelope);
        ValidateMasterKeyProtectors(keyProtectors);

        long unlockEpoch;
        lock (_gate)
        {
            if (_state != VaultState.Locked || _vaultMasterKey is not null)
                return VaultUnlockResult.Fail("InvalidState");
            unlockEpoch = _lockEpoch;
            _state = VaultState.Unlocking;
        }

        byte[] body = SerializeEnvelopeBody(envelope.VaultId, envelope.WrappedMasterKeys);
        byte[]? accepted = null;
        try
        {
            foreach (WrappedFileKeyRecord record in envelope.WrappedMasterKeys)
            {
                foreach (IFileKeyProtector protector in keyProtectors.Where(p => p.ProtectionModeId == record.ProtectionModeId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[]? candidate = await protector.TryUnwrapAsync(CloneRecord(record), cancellationToken).ConfigureAwait(false);
                    if (candidate is null) continue;
                    if (candidate.Length != MasterKeySize)
                    {
                        CryptographicOperations.ZeroMemory(candidate);
                        continue;
                    }

                    byte[] expectedTag = ComputeEnvelopeTag(candidate, body);
                    bool valid = CryptographicOperations.FixedTimeEquals(expectedTag, envelope.AuthenticationTag);
                    CryptographicOperations.ZeroMemory(expectedTag);
                    if (!valid)
                    {
                        CryptographicOperations.ZeroMemory(candidate);
                        continue;
                    }

                    accepted = candidate;
                    break;
                }
                if (accepted is not null) break;
            }

            if (accepted is null)
            {
                lock (_gate)
                {
                    if (_state == VaultState.Unlocking && _lockEpoch == unlockEpoch)
                        _state = VaultState.Locked;
                }
                return VaultUnlockResult.Fail("CredentialOrEnvelopeRejected");
            }

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_state != VaultState.Unlocking || _lockEpoch != unlockEpoch)
                    return VaultUnlockResult.Fail("LockStateChanged");
                _operationEpochCancellation?.Dispose();
                _operationEpochCancellation = new CancellationTokenSource();
                _vaultMasterKey = accepted;
                accepted = null;
                _vaultId = envelope.VaultId;
                _lastActivityUtc = DateTimeOffset.UtcNow;
                _state = VaultState.Unlocked;
            }
            return VaultUnlockResult.Success(envelope.VaultId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_state == VaultState.Unlocking && _lockEpoch == unlockEpoch)
                    _state = VaultState.Locked;
            }
            throw;
        }
        catch
        {
            lock (_gate)
            {
                if (_state == VaultState.Unlocking && _lockEpoch == unlockEpoch)
                    _state = VaultState.Locked;
            }
            throw;
        }
        finally
        {
            if (accepted is not null) CryptographicOperations.ZeroMemory(accepted);
            CryptographicOperations.ZeroMemory(body);
        }
    }

    internal VaultOperationSession AcquireOperationSession()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state != VaultState.Unlocked || _vaultMasterKey is null || _vaultId == Guid.Empty || _operationEpochCancellation is null)
                throw new InvalidOperationException("The Sentinel Vault is locked.");
            _lastActivityUtc = DateTimeOffset.UtcNow;
            return new VaultOperationSession(_lockEpoch, _vaultId, _operationEpochCancellation.Token);
        }
    }

    internal bool IsOperationSessionValid(VaultOperationSession session)
    {
        lock (_gate)
        {
            return !_disposed &&
                   _state == VaultState.Unlocked &&
                   _vaultMasterKey is not null &&
                   _operationEpochCancellation is not null &&
                   _vaultId != Guid.Empty &&
                   _vaultId == session.VaultId &&
                   _lockEpoch == session.LockEpoch &&
                   _operationEpochCancellation.Token == session.CancellationToken &&
                   !session.CancellationToken.IsCancellationRequested;
        }
    }

    internal VaultItemKeyLease CreateItemKey(Guid itemId)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("A non-empty vault item ID is required.", nameof(itemId));
        byte[] vmk = CopyUnlockedMasterKey(out Guid vaultId);
        byte[] itemKey = RandomNumberGenerator.GetBytes(ItemKeySize);
        byte[] nonce = RandomNumberGenerator.GetBytes(ItemNonceSize);
        byte[] ciphertext = new byte[ItemKeySize];
        byte[] tag = new byte[ItemTagSize];
        byte[] aad = BuildItemAad(vaultId, itemId);
        try
        {
            using AesGcm aes = new(vmk, ItemTagSize);
            aes.Encrypt(nonce, itemKey, ciphertext, tag, aad);
            byte[] wrapped = new byte[ItemKeySize + ItemTagSize];
            ciphertext.CopyTo(wrapped, 0);
            tag.CopyTo(wrapped, ItemKeySize);
            byte[] durableNonce = nonce.ToArray();
            Touch();
            return new VaultItemKeyLease(
                itemKey,
                new VaultWrappedItemKey(VaultFormatVersion, vaultId, itemId, durableNonce, wrapped));
        }
        catch
        {
            CryptographicOperations.ZeroMemory(itemKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    internal VaultItemKeyLease? TryOpenItemKey(VaultWrappedItemKey wrappedItemKey)
    {
        ArgumentNullException.ThrowIfNull(wrappedItemKey);
        if (wrappedItemKey.Version != VaultFormatVersion ||
            wrappedItemKey.VaultId == Guid.Empty || wrappedItemKey.ItemId == Guid.Empty ||
            wrappedItemKey.Nonce.Length != ItemNonceSize ||
            wrappedItemKey.WrappedItemKey.Length != ItemKeySize + ItemTagSize)
            return null;

        byte[] vmk = CopyUnlockedMasterKey(out Guid vaultId);
        if (vaultId != wrappedItemKey.VaultId)
        {
            CryptographicOperations.ZeroMemory(vmk);
            return null;
        }

        byte[] plaintext = new byte[ItemKeySize];
        byte[] aad = BuildItemAad(vaultId, wrappedItemKey.ItemId);
        try
        {
            using AesGcm aes = new(vmk, ItemTagSize);
            aes.Decrypt(
                wrappedItemKey.Nonce,
                wrappedItemKey.WrappedItemKey.AsSpan(0, ItemKeySize),
                wrappedItemKey.WrappedItemKey.AsSpan(ItemKeySize, ItemTagSize),
                plaintext,
                aad);
            Touch();
            return new VaultItemKeyLease(plaintext, CloneWrappedItemKey(wrappedItemKey));
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    internal bool LockIfInactive(TimeSpan timeout, DateTimeOffset nowUtc)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        byte[]? key;
        CancellationTokenSource? operations;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state != VaultState.Unlocked || _vaultMasterKey is null) return false;
            if (nowUtc - _lastActivityUtc < timeout) return false;
            key = TransitionToLockedUnderGate(out operations);
        }
        CancelOperations(operations);
        if (key is not null) CryptographicOperations.ZeroMemory(key);
        return true;
    }

    internal void Lock()
    {
        byte[]? key;
        CancellationTokenSource? operations;
        lock (_gate)
        {
            if (_state == VaultState.Locked && _vaultMasterKey is null) return;
            key = TransitionToLockedUnderGate(out operations);
        }
        CancelOperations(operations);
        if (key is not null) CryptographicOperations.ZeroMemory(key);
    }

    internal void HandleWindowsSessionLocked() => Lock();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }
        Lock();
        lock (_gate) _disposed = true;
    }

    private byte[]? TransitionToLockedUnderGate(out CancellationTokenSource? operations)
    {
        _lockEpoch = checked(_lockEpoch + 1);
        _state = VaultState.Locking;
        byte[]? key = _vaultMasterKey;
        operations = _operationEpochCancellation;
        _operationEpochCancellation = null;
        _vaultMasterKey = null;
        _vaultId = Guid.Empty;
        _lastActivityUtc = default;
        _state = VaultState.Locked;
        return key;
    }

    private static void CancelOperations(CancellationTokenSource? operations)
    {
        if (operations is null) return;
        try { operations.Cancel(); }
        finally { operations.Dispose(); }
    }

    private byte[] CopyUnlockedMasterKey(out Guid vaultId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state != VaultState.Unlocked || _vaultMasterKey is null || _vaultId == Guid.Empty)
                throw new InvalidOperationException("The Sentinel Vault is locked.");
            vaultId = _vaultId;
            return _vaultMasterKey.ToArray();
        }
    }

    private void Touch()
    {
        lock (_gate)
        {
            if (_state == VaultState.Unlocked) _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    private static void ValidateMasterKeyProtectors(IReadOnlyList<IFileKeyProtector> protectors)
    {
        if (protectors is null || protectors.Count is < 1 or > MaximumKeyRecords)
            throw new ArgumentException("A vault requires one to three master-key protection modes.", nameof(protectors));
        HashSet<ushort> modes = new();
        foreach (IFileKeyProtector? protector in protectors)
        {
            if (protector is null || protector.ProtectionModeId is < 1 or > 3 || !modes.Add(protector.ProtectionModeId))
                throw new ArgumentException("Vault master-key protectors must use unique Windows, password, or recovery modes.", nameof(protectors));
        }
    }

    private static void ValidateMasterKeyRecord(WrappedFileKeyRecord record, ushort expectedMode)
    {
        if (record.RecordVersion != 1 || record.ProtectionModeId != expectedMode ||
            record.ProtectionModeId is < 1 or > 3 || record.WrappingAlgorithmId == 0 ||
            record.Parameters is null || record.WrappedDek is null ||
            record.Parameters.Length > MaximumRecordSection ||
            record.WrappedDek.Length is 0 or > MaximumRecordSection)
            throw new InvalidDataException("A vault master-key protection record was invalid.");
    }

    private static void ValidateEnvelopeShape(VaultMasterKeyEnvelope envelope)
    {
        if (envelope.Version != VaultFormatVersion || envelope.VaultId == Guid.Empty ||
            envelope.AuthenticationTag is null || envelope.AuthenticationTag.Length != 32 ||
            envelope.WrappedMasterKeys is null || envelope.WrappedMasterKeys.Count is < 1 or > MaximumKeyRecords)
            throw new InvalidDataException("The vault master-key envelope is invalid.");

        HashSet<ushort> modes = new();
        foreach (WrappedFileKeyRecord record in envelope.WrappedMasterKeys)
        {
            ValidateMasterKeyRecord(record, record.ProtectionModeId);
            if (!modes.Add(record.ProtectionModeId))
                throw new InvalidDataException("The vault master-key envelope contains a duplicate protection mode.");
        }
    }

    private static byte[] SerializeEnvelopeBody(Guid vaultId, IReadOnlyList<WrappedFileKeyRecord> records)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(VaultFormatVersion);
        writer.Write(vaultId.ToByteArray());
        writer.Write((ushort)records.Count);
        foreach (WrappedFileKeyRecord record in records)
        {
            writer.Write(record.RecordVersion);
            writer.Write(record.ProtectionModeId);
            writer.Write(record.WrappingAlgorithmId);
            writer.Write(record.Parameters.Length);
            writer.Write(record.Parameters);
            writer.Write(record.WrappedDek.Length);
            writer.Write(record.WrappedDek);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] ComputeEnvelopeTag(byte[] vmk, byte[] body)
    {
        byte[] authKey = HMACSHA256.HashData(vmk, EnvelopeKeyContext);
        try
        {
            using HMACSHA256 hmac = new(authKey);
            hmac.TransformBlock(EnvelopeAuthContext, 0, EnvelopeAuthContext.Length, null, 0);
            hmac.TransformFinalBlock(body, 0, body.Length);
            return hmac.Hash ?? throw new CryptographicException("Vault envelope authentication failed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authKey);
        }
    }

    private static byte[] BuildItemAad(Guid vaultId, Guid itemId)
    {
        byte[] aad = new byte[ItemWrapContext.Length + 32];
        ItemWrapContext.CopyTo(aad, 0);
        vaultId.ToByteArray().CopyTo(aad, ItemWrapContext.Length);
        itemId.ToByteArray().CopyTo(aad, ItemWrapContext.Length + 16);
        return aad;
    }

    private static WrappedFileKeyRecord CloneRecord(WrappedFileKeyRecord record) =>
        new(record.RecordVersion, record.ProtectionModeId, record.WrappingAlgorithmId,
            record.Parameters.ToArray(), record.WrappedDek.ToArray());

    private static VaultWrappedItemKey CloneWrappedItemKey(VaultWrappedItemKey record) =>
        new(record.Version, record.VaultId, record.ItemId, record.Nonce.ToArray(), record.WrappedItemKey.ToArray());

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal enum VaultState
{
    Locked,
    Unlocking,
    Unlocked,
    Locking,
    RecoveryRequired,
    Corrupt
}

internal readonly record struct VaultOperationSession(
    long LockEpoch,
    Guid VaultId,
    CancellationToken CancellationToken);

internal sealed record VaultMasterKeyEnvelope(
    int Version,
    Guid VaultId,
    IReadOnlyList<WrappedFileKeyRecord> WrappedMasterKeys,
    byte[] AuthenticationTag);

internal sealed record VaultWrappedItemKey(
    int Version,
    Guid VaultId,
    Guid ItemId,
    byte[] Nonce,
    byte[] WrappedItemKey);

internal sealed record VaultUnlockResult(bool Succeeded, string Code, Guid VaultId)
{
    internal static VaultUnlockResult Success(Guid vaultId) => new(true, "Unlocked", vaultId);
    internal static VaultUnlockResult Fail(string code) => new(false, code, Guid.Empty);
}

internal sealed class VaultItemKeyLease : IDisposable
{
    private byte[]? _itemKey;

    internal VaultItemKeyLease(byte[] itemKey, VaultWrappedItemKey wrappedItemKey)
    {
        if (itemKey is null || itemKey.Length != 32) throw new ArgumentException("A 256-bit vault item key is required.", nameof(itemKey));
        _itemKey = itemKey;
        WrappedItemKey = wrappedItemKey ?? throw new ArgumentNullException(nameof(wrappedItemKey));
    }

    internal ReadOnlyMemory<byte> ItemKey => _itemKey ?? throw new ObjectDisposedException(nameof(VaultItemKeyLease));
    internal VaultWrappedItemKey WrappedItemKey { get; }

    public void Dispose()
    {
        byte[]? key = Interlocked.Exchange(ref _itemKey, null);
        if (key is not null) CryptographicOperations.ZeroMemory(key);
    }
}
