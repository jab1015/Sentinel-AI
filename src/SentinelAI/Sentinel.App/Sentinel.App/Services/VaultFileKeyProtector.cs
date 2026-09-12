using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class VaultFileKeyProtector : IFileKeyProtector, IDisposable
{
    internal const ushort ModeId = 4;
    internal const ushort WrappingAlgorithmVaultItemKeyAesGcm = 2;
    private const byte ParameterVersion = 2;
    private const int DekSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int WrappedKeySize = DekSize + TagSize;
    private const int ParameterLength = 1 + 16 + 16 + NonceSize + NonceSize;
    private const int WrappedDekLength = WrappedKeySize + WrappedKeySize;
    private static readonly byte[] ContentWrapContext = Encoding.ASCII.GetBytes("SentinelAI.VaultContainerDEK.v1");

    private readonly SentinelVaultService _vault;
    private readonly Guid _expectedItemId;
    private readonly VaultWrappedItemKey? _encryptionRecord;
    private byte[]? _itemWrappingKey;

    private VaultFileKeyProtector(
        SentinelVaultService vault,
        Guid expectedItemId,
        VaultWrappedItemKey? encryptionRecord,
        byte[]? itemWrappingKey)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        if (expectedItemId == Guid.Empty)
            throw new ArgumentException("A non-empty Vault item ID is required.", nameof(expectedItemId));
        _expectedItemId = expectedItemId;
        _encryptionRecord = encryptionRecord;
        _itemWrappingKey = itemWrappingKey;
    }

    public ushort ProtectionModeId => ModeId;

    internal static VaultFileKeyProtector ForEncryption(SentinelVaultService vault, VaultItemKeyLease itemKeyLease)
    {
        ArgumentNullException.ThrowIfNull(itemKeyLease);
        VaultWrappedItemKey wrapped = itemKeyLease.WrappedItemKey;
        if (wrapped.ItemId == Guid.Empty || wrapped.VaultId == Guid.Empty || itemKeyLease.ItemKey.Length != DekSize)
            throw new CryptographicException("The Vault item-key lease was invalid.");
        if (vault.State != VaultState.Unlocked || vault.VaultId != wrapped.VaultId)
            throw new InvalidOperationException("The Sentinel Vault is locked or does not match the item-key lease.");

        return new VaultFileKeyProtector(
            vault,
            wrapped.ItemId,
            CloneWrappedItemKey(wrapped),
            itemKeyLease.ItemKey.ToArray());
    }

    internal static VaultFileKeyProtector ForOpening(SentinelVaultService vault, Guid itemId) =>
        new(vault, itemId, null, null);

    public ValueTask<WrappedFileKeyRecord> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dataEncryptionKey.Length != DekSize)
            throw new CryptographicException("Vault mode requires a 256-bit container data-encryption key.");

        VaultWrappedItemKey? wrappedItemKey = _encryptionRecord;
        byte[]? itemWrappingKey = _itemWrappingKey;
        if (wrappedItemKey is null || itemWrappingKey is null)
            throw new InvalidOperationException("This Vault key protector was not created for encryption.");
        if (_vault.State != VaultState.Unlocked || _vault.VaultId != wrappedItemKey.VaultId)
            throw new InvalidOperationException("The Sentinel Vault is locked or changed during encryption.");

        byte[] contentNonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] contentCiphertext = new byte[DekSize];
        byte[] contentTag = new byte[TagSize];
        byte[] aad = BuildContentAad(wrappedItemKey.VaultId, wrappedItemKey.ItemId);
        try
        {
            using AesGcm aes = new(itemWrappingKey, TagSize);
            aes.Encrypt(contentNonce, dataEncryptionKey.Span, contentCiphertext, contentTag, aad);

            byte[] parameters = new byte[ParameterLength];
            parameters[0] = ParameterVersion;
            wrappedItemKey.VaultId.ToByteArray().CopyTo(parameters, 1);
            wrappedItemKey.ItemId.ToByteArray().CopyTo(parameters, 17);
            wrappedItemKey.Nonce.CopyTo(parameters, 33);
            contentNonce.CopyTo(parameters, 45);

            byte[] wrappedDek = new byte[WrappedDekLength];
            wrappedItemKey.WrappedItemKey.CopyTo(wrappedDek, 0);
            contentCiphertext.CopyTo(wrappedDek, WrappedKeySize);
            contentTag.CopyTo(wrappedDek, WrappedKeySize + DekSize);

            return ValueTask.FromResult(new WrappedFileKeyRecord(
                1,
                ModeId,
                WrappingAlgorithmVaultItemKeyAesGcm,
                parameters,
                wrappedDek));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentNonce);
            CryptographicOperations.ZeroMemory(contentCiphertext);
            CryptographicOperations.ZeroMemory(contentTag);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    public ValueTask<byte[]?> TryUnwrapAsync(
        WrappedFileKeyRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null || record.RecordVersion != 1 || record.ProtectionModeId != ModeId ||
            record.WrappingAlgorithmId != WrappingAlgorithmVaultItemKeyAesGcm ||
            record.Parameters.Length != ParameterLength || record.Parameters[0] != ParameterVersion ||
            record.WrappedDek.Length != WrappedDekLength)
            return ValueTask.FromResult<byte[]?>(null);

        Guid vaultId = new(record.Parameters.AsSpan(1, 16));
        Guid itemId = new(record.Parameters.AsSpan(17, 16));
        if (vaultId == Guid.Empty || itemId == Guid.Empty || itemId != _expectedItemId)
            return ValueTask.FromResult<byte[]?>(null);

        byte[] itemNonce = record.Parameters.AsSpan(33, NonceSize).ToArray();
        byte[] contentNonce = record.Parameters.AsSpan(45, NonceSize).ToArray();
        byte[] wrappedItemBytes = record.WrappedDek.AsSpan(0, WrappedKeySize).ToArray();
        byte[] aad = BuildContentAad(vaultId, itemId);
        byte[] plaintext = new byte[DekSize];
        try
        {
            VaultWrappedItemKey wrappedItemKey = new(1, vaultId, itemId, itemNonce, wrappedItemBytes);
            using VaultItemKeyLease? itemLease = _vault.TryOpenItemKey(wrappedItemKey);
            if (itemLease is null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                return ValueTask.FromResult<byte[]?>(null);
            }

            using AesGcm aes = new(itemLease.ItemKey.Span, TagSize);
            aes.Decrypt(
                contentNonce,
                record.WrappedDek.AsSpan(WrappedKeySize, DekSize),
                record.WrappedDek.AsSpan(WrappedKeySize + DekSize, TagSize),
                plaintext,
                aad);
            return ValueTask.FromResult<byte[]?>(plaintext);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return ValueTask.FromResult<byte[]?>(null);
        }
        catch (InvalidOperationException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return ValueTask.FromResult<byte[]?>(null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(itemNonce);
            CryptographicOperations.ZeroMemory(contentNonce);
            CryptographicOperations.ZeroMemory(wrappedItemBytes);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    public void Dispose()
    {
        byte[]? key = Interlocked.Exchange(ref _itemWrappingKey, null);
        if (key is not null) CryptographicOperations.ZeroMemory(key);
    }

    private static byte[] BuildContentAad(Guid vaultId, Guid itemId)
    {
        byte[] aad = new byte[ContentWrapContext.Length + 32];
        ContentWrapContext.CopyTo(aad, 0);
        vaultId.ToByteArray().CopyTo(aad, ContentWrapContext.Length);
        itemId.ToByteArray().CopyTo(aad, ContentWrapContext.Length + 16);
        return aad;
    }

    private static VaultWrappedItemKey CloneWrappedItemKey(VaultWrappedItemKey record) =>
        new(record.Version, record.VaultId, record.ItemId, record.Nonce.ToArray(), record.WrappedItemKey.ToArray());
}
