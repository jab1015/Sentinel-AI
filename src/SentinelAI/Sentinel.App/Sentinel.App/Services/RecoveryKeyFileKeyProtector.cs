using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class RecoveryKeyFileKeyProtector : IFileKeyProtector, IDisposable
{
    internal const ushort ModeId = 3;
    internal const ushort WrappingAlgorithmAesGcm = 1;
    private const byte ParameterVersion = 1;
    private const int RecoverySecretSize = 32;
    private const int DekSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AeadContext = Encoding.ASCII.GetBytes("SentinelAI.RecoveryDEK.v1");

    private readonly byte[] _recoverySecret;
    private bool _disposed;

    internal RecoveryKeyFileKeyProtector(ReadOnlySpan<byte> recoverySecret)
    {
        if (recoverySecret.Length != RecoverySecretSize)
            throw new ArgumentException("Recovery key material must contain exactly 256 bits.", nameof(recoverySecret));
        _recoverySecret = recoverySecret.ToArray();
    }

    public ushort ProtectionModeId => ModeId;

    public ValueTask<WrappedFileKeyRecord> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (dataEncryptionKey.Length != DekSize)
            throw new ArgumentException("A 256-bit file data-encryption key is required.", nameof(dataEncryptionKey));

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] parameters = new byte[1 + NonceSize];
        parameters[0] = ParameterVersion;
        nonce.CopyTo(parameters, 1);
        byte[] ciphertext = new byte[DekSize];
        byte[] tag = new byte[TagSize];
        byte[] aad = BuildAad(parameters);
        try
        {
            using AesGcm aes = new(_recoverySecret, TagSize);
            aes.Encrypt(nonce, dataEncryptionKey.Span, ciphertext, tag, aad);
            byte[] wrapped = new byte[DekSize + TagSize];
            ciphertext.CopyTo(wrapped, 0);
            tag.CopyTo(wrapped, DekSize);
            return ValueTask.FromResult(new WrappedFileKeyRecord(1, ModeId, WrappingAlgorithmAesGcm, parameters, wrapped));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    public ValueTask<byte[]?> TryUnwrapAsync(
        WrappedFileKeyRecord record,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null || record.RecordVersion != 1 || record.ProtectionModeId != ModeId ||
            record.WrappingAlgorithmId != WrappingAlgorithmAesGcm ||
            record.Parameters.Length != 1 + NonceSize || record.Parameters[0] != ParameterVersion ||
            record.WrappedDek.Length != DekSize + TagSize)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        byte[] nonce = record.Parameters.AsSpan(1, NonceSize).ToArray();
        byte[] plaintext = new byte[DekSize];
        byte[] aad = BuildAad(record.Parameters);
        try
        {
            using AesGcm aes = new(_recoverySecret, TagSize);
            aes.Decrypt(nonce, record.WrappedDek.AsSpan(0, DekSize), record.WrappedDek.AsSpan(DekSize, TagSize), plaintext, aad);
            return ValueTask.FromResult<byte[]?>(plaintext);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return ValueTask.FromResult<byte[]?>(null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_recoverySecret);
        _disposed = true;
    }

    private static byte[] BuildAad(byte[] parameters)
    {
        byte[] aad = new byte[AeadContext.Length + 4 + parameters.Length];
        AeadContext.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(aad.AsSpan(AeadContext.Length, 2), ModeId);
        BinaryPrimitives.WriteUInt16LittleEndian(aad.AsSpan(AeadContext.Length + 2, 2), WrappingAlgorithmAesGcm);
        parameters.CopyTo(aad, AeadContext.Length + 4);
        return aad;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
