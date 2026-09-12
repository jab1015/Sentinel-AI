using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class VaultFileKeyProtector : IFileKeyProtector, IDisposable
{
    internal const ushort ModeId = 4;
    internal const ushort WrappingAlgorithmVaultMasterKeyAesGcm = 1;
    private const byte ParameterVersion = 1;
    private const int ParameterLength = 1 + 16 + 16 + 12;
    private const int WrappedKeyLength = 48;

    private readonly SentinelVaultService _vault;
    private readonly Guid _expectedItemId;
    private readonly VaultWrappedItemKey? _encryptionRecord;
    private byte[]? _expectedDataKey;

    private VaultFileKeyProtector(
        SentinelVaultService vault,
        Guid expectedItemId,
        byte[]? expectedDataKey,
        VaultWrappedItemKey? encryptionRecord)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        if (expectedItemId == Guid.Empty) throw new ArgumentException("A non-empty Vault item ID is required.", nameof(expectedItemId));
        _expectedItemId = expectedItemId;
        _expectedDataKey = expectedDataKey;
        _encryptionRecord = encryptionRecord;
    }

    public ushort ProtectionModeId => ModeId;

    internal static VaultFileKeyProtector ForEncryption(SentinelVaultService vault, VaultItemKeyLease itemKeyLease)
    {
        ArgumentNullException.ThrowIfNull(itemKeyLease);
        VaultWrappedItemKey wrapped = itemKeyLease.WrappedItemKey;
        return new VaultFileKeyProtector(
            vault,
            wrapped.ItemId,
            itemKeyLease.ItemKey.ToArray(),
            CloneWrappedItemKey(wrapped));
    }

    internal static VaultFileKeyProtector ForOpening(SentinelVaultService vault, Guid itemId) =>
        new(vault, itemId, null, null);

    public ValueTask<WrappedFileKeyRecord> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? expected = _expectedDataKey;
        VaultWrappedItemKey? wrapped = _encryptionRecord;
        if (expected is null || wrapped is null)
            throw new InvalidOperationException("This Vault key protector was not created for encryption.");
        if (dataEncryptionKey.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(dataEncryptionKey.Span, expected))
            throw new CryptographicException("The container DEK did not match the Vault-bound item DEK.");
        if (_vault.State != VaultState.Unlocked || _vault.VaultId != wrapped.VaultId)
            throw new InvalidOperationException("The Sentinel Vault is locked or changed during encryption.");

        byte[] parameters = new byte[ParameterLength];
        parameters[0] = ParameterVersion;
        wrapped.VaultId.ToByteArray().CopyTo(parameters, 1);
        wrapped.ItemId.ToByteArray().CopyTo(parameters, 17);
        wrapped.Nonce.CopyTo(parameters, 33);
        return ValueTask.FromResult(new WrappedFileKeyRecord(
            1,
            ModeId,
            WrappingAlgorithmVaultMasterKeyAesGcm,
            parameters,
            wrapped.WrappedItemKey.ToArray()));
    }

    public ValueTask<byte[]?> TryUnwrapAsync(
        WrappedFileKeyRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null || record.RecordVersion != 1 || record.ProtectionModeId != ModeId ||
            record.WrappingAlgorithmId != WrappingAlgorithmVaultMasterKeyAesGcm ||
            record.Parameters.Length != ParameterLength || record.Parameters[0] != ParameterVersion ||
            record.WrappedDek.Length != WrappedKeyLength)
            return ValueTask.FromResult<byte[]?>(null);

        Guid vaultId = new(record.Parameters.AsSpan(1, 16));
        Guid itemId = new(record.Parameters.AsSpan(17, 16));
        if (vaultId == Guid.Empty || itemId == Guid.Empty || itemId != _expectedItemId)
            return ValueTask.FromResult<byte[]?>(null);

        byte[] nonce = record.Parameters.AsSpan(33, 12).ToArray();
        try
        {
            VaultWrappedItemKey wrapped = new(1, vaultId, itemId, nonce, record.WrappedDek.ToArray());
            using VaultItemKeyLease? lease = _vault.TryOpenItemKey(wrapped);
            if (lease is null) return ValueTask.FromResult<byte[]?>(null);
            return ValueTask.FromResult<byte[]?>(lease.ItemKey.ToArray());
        }
        catch (InvalidOperationException)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public void Dispose()
    {
        byte[]? key = Interlocked.Exchange(ref _expectedDataKey, null);
        if (key is not null) CryptographicOperations.ZeroMemory(key);
    }

    private static VaultWrappedItemKey CloneWrappedItemKey(VaultWrappedItemKey record) =>
        new(record.Version, record.VaultId, record.ItemId, record.Nonce.ToArray(), record.WrappedItemKey.ToArray());
}
