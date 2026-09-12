using Konscious.Security.Cryptography;
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class PasswordFileKeyProtector : IFileKeyProtector, IDisposable
{
    internal const ushort ModeId = 2;
    internal const ushort WrappingAlgorithmArgon2idAesGcm = 1;
    internal const int DefaultMemoryKiB = 64 * 1024;
    internal const int DefaultIterations = 3;
    internal const int DefaultParallelism = 4;

    private const byte ParameterVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int KekSize = 32;
    private const int DekSize = 32;
    private const int TagSize = 16;
    private const int ParameterSize = 1 + SaltSize + 4 + 4 + 4 + NonceSize;
    private const int MinimumMemoryKiB = 64 * 1024;
    private const int MaximumMemoryKiB = 256 * 1024;
    private const int MinimumIterations = 3;
    private const int MaximumIterations = 10;
    private const int MinimumParallelism = 1;
    private const int MaximumParallelism = 8;
    private static readonly byte[] AeadContext = Encoding.ASCII.GetBytes("SentinelAI.PasswordDEK.v1");

    private readonly char[] _password;
    private readonly int _memoryKiB;
    private readonly int _iterations;
    private readonly int _parallelism;
    private bool _disposed;

    internal PasswordFileKeyProtector(
        ReadOnlySpan<char> password,
        int memoryKiB = DefaultMemoryKiB,
        int iterations = DefaultIterations,
        int parallelism = DefaultParallelism)
    {
        if (password.IsEmpty) throw new ArgumentException("A password is required.", nameof(password));
        ValidateKdfParameters(memoryKiB, iterations, parallelism);
        _password = password.ToArray();
        _memoryKiB = memoryKiB;
        _iterations = iterations;
        _parallelism = parallelism;
    }

    public ushort ProtectionModeId => ModeId;

    public async ValueTask<WrappedFileKeyRecord> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (dataEncryptionKey.Length != DekSize)
            throw new ArgumentException("A 256-bit file data-encryption key is required.", nameof(dataEncryptionKey));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] parameters = BuildParameters(salt, nonce, _memoryKiB, _iterations, _parallelism);
        byte[] kek = await DeriveKekAsync(salt, _memoryKiB, _iterations, _parallelism, cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] ciphertext = new byte[DekSize];
            byte[] tag = new byte[TagSize];
            try
            {
                using AesGcm aes = new(kek, TagSize);
                aes.Encrypt(nonce, dataEncryptionKey.Span, ciphertext, tag, BuildAad(parameters));
                byte[] wrapped = new byte[DekSize + TagSize];
                ciphertext.CopyTo(wrapped, 0);
                tag.CopyTo(wrapped, DekSize);
                return new WrappedFileKeyRecord(1, ModeId, WrappingAlgorithmArgon2idAesGcm, parameters, wrapped);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public async ValueTask<byte[]?> TryUnwrapAsync(
        WrappedFileKeyRecord record,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null || record.RecordVersion != 1 || record.ProtectionModeId != ModeId ||
            record.WrappingAlgorithmId != WrappingAlgorithmArgon2idAesGcm ||
            record.Parameters.Length != ParameterSize || record.WrappedDek.Length != DekSize + TagSize)
        {
            return null;
        }

        if (!TryParseParameters(record.Parameters, out byte[] salt, out byte[] nonce,
            out int memoryKiB, out int iterations, out int parallelism))
        {
            return null;
        }

        byte[] kek = await DeriveKekAsync(salt, memoryKiB, iterations, parallelism, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = new byte[DekSize];
        try
        {
            using AesGcm aes = new(kek, TagSize);
            aes.Decrypt(nonce, record.WrappedDek.AsSpan(0, DekSize), record.WrappedDek.AsSpan(DekSize, TagSize), plaintext,
                BuildAad(record.Parameters));
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Array.Clear(_password, 0, _password.Length);
        _disposed = true;
    }

    private async Task<byte[]> DeriveKekAsync(
        byte[] salt,
        int memoryKiB,
        int iterations,
        int parallelism,
        CancellationToken cancellationToken)
    {
        byte[] passwordBytes = EncodePasswordUtf8();
        try
        {
            using Argon2id argon2 = new(passwordBytes)
            {
                Salt = salt.ToArray(),
                MemorySize = memoryKiB,
                Iterations = iterations,
                DegreeOfParallelism = parallelism
            };

            Task<byte[]> derivation = argon2.GetBytesAsync(KekSize);
            return await derivation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private byte[] EncodePasswordUtf8()
    {
        int length = Encoding.UTF8.GetByteCount(_password.AsSpan());
        byte[] bytes = new byte[length];
        Encoding.UTF8.GetBytes(_password.AsSpan(), bytes.AsSpan());
        return bytes;
    }

    private static byte[] BuildParameters(byte[] salt, byte[] nonce, int memoryKiB, int iterations, int parallelism)
    {
        byte[] parameters = new byte[ParameterSize];
        parameters[0] = ParameterVersion;
        salt.CopyTo(parameters, 1);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(1 + SaltSize, 4), memoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(1 + SaltSize + 4, 4), iterations);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(1 + SaltSize + 8, 4), parallelism);
        nonce.CopyTo(parameters, 1 + SaltSize + 12);
        return parameters;
    }

    private static bool TryParseParameters(
        byte[] parameters,
        out byte[] salt,
        out byte[] nonce,
        out int memoryKiB,
        out int iterations,
        out int parallelism)
    {
        salt = Array.Empty<byte>();
        nonce = Array.Empty<byte>();
        memoryKiB = 0;
        iterations = 0;
        parallelism = 0;
        if (parameters.Length != ParameterSize || parameters[0] != ParameterVersion) return false;

        salt = parameters.AsSpan(1, SaltSize).ToArray();
        memoryKiB = BinaryPrimitives.ReadInt32LittleEndian(parameters.AsSpan(1 + SaltSize, 4));
        iterations = BinaryPrimitives.ReadInt32LittleEndian(parameters.AsSpan(1 + SaltSize + 4, 4));
        parallelism = BinaryPrimitives.ReadInt32LittleEndian(parameters.AsSpan(1 + SaltSize + 8, 4));
        nonce = parameters.AsSpan(1 + SaltSize + 12, NonceSize).ToArray();
        try
        {
            ValidateKdfParameters(memoryKiB, iterations, parallelism);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            salt = Array.Empty<byte>();
            nonce = Array.Empty<byte>();
            return false;
        }
    }

    private static void ValidateKdfParameters(int memoryKiB, int iterations, int parallelism)
    {
        if (memoryKiB is < MinimumMemoryKiB or > MaximumMemoryKiB)
            throw new ArgumentOutOfRangeException(nameof(memoryKiB));
        if (iterations is < MinimumIterations or > MaximumIterations)
            throw new ArgumentOutOfRangeException(nameof(iterations));
        if (parallelism is < MinimumParallelism or > MaximumParallelism)
            throw new ArgumentOutOfRangeException(nameof(parallelism));
    }

    private static byte[] BuildAad(byte[] parameters)
    {
        byte[] aad = new byte[AeadContext.Length + 4 + parameters.Length];
        AeadContext.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(aad.AsSpan(AeadContext.Length, 2), ModeId);
        BinaryPrimitives.WriteUInt16LittleEndian(aad.AsSpan(AeadContext.Length + 2, 2), WrappingAlgorithmArgon2idAesGcm);
        parameters.CopyTo(aad, AeadContext.Length + 4);
        return aad;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
