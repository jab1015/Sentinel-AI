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

internal static class SentinelEncryptedContainerV1
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SNTLENC1");
    internal const int MinimumChunkSize = 64 * 1024;
    internal const int DefaultChunkSize = 1024 * 1024;
    internal const int MaximumChunkSize = 8 * 1024 * 1024;

    private const ushort FormatVersion = 1;
    private const ushort AlgorithmAes256Gcm = 1;
    private const ushort KeyRecordVersion = 1;
    private const int FixedHeaderSize = 50;
    private const int HeaderTagSize = 16;
    private const int GcmTagSize = 16;
    private const int DekSize = 32;
    private const int NoncePrefixSize = 8;
    private const int NonceSize = 12;
    private const int MaximumHeaderSize = 256 * 1024;
    private const int MaximumKeyRecords = 16;
    private const int MaximumKeyParameters = 64 * 1024;
    private const int MaximumWrappedDek = 64 * 1024;

    internal static void ValidateChunkSize(int chunkSize)
    {
        if (chunkSize is < MinimumChunkSize or > MaximumChunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
    }

    internal static bool TryValidateProtectors(IReadOnlyList<IFileKeyProtector>? protectors, out string error)
    {
        error = string.Empty;
        if (protectors is null || protectors.Count is < 1 or > MaximumKeyRecords)
        {
            error = "At least one and no more than 16 key protectors are required.";
            return false;
        }

        HashSet<ushort> modes = new();
        foreach (IFileKeyProtector? protector in protectors)
        {
            if (protector is null || protector.ProtectionModeId is < 1 or > 4 || !modes.Add(protector.ProtectionModeId))
            {
                error = "Key protectors must be non-null and use unique supported protection mode IDs.";
                return false;
            }
        }
        return true;
    }

    internal static long CalculateChunkCount(long length, int chunkSize)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        ValidateChunkSize(chunkSize);
        return length == 0 ? 0 : (length / chunkSize) + (length % chunkSize == 0 ? 0 : 1);
    }

    internal static async Task<ContainerWriteHeader> CreateHeaderAsync(
        long originalLength,
        int chunkSize,
        IReadOnlyList<IFileKeyProtector> protectors,
        byte[] dataEncryptionKey,
        byte[] noncePrefix,
        CancellationToken cancellationToken)
    {
        if (dataEncryptionKey is null || dataEncryptionKey.Length != DekSize)
            throw new ArgumentException("A 256-bit data-encryption key is required.", nameof(dataEncryptionKey));
        if (noncePrefix is null || noncePrefix.Length != NoncePrefixSize)
            throw new ArgumentException("An 8-byte nonce prefix is required.", nameof(noncePrefix));
        if (!TryValidateProtectors(protectors, out string protectorError))
            throw new InvalidDataException(protectorError);

        long chunkCount = CalculateChunkCount(originalLength, chunkSize);
        if ((ulong)chunkCount > uint.MaxValue)
            throw new InvalidDataException("The source requires more chunks than the version 1 nonce space permits.");

        List<WrappedFileKeyRecord> keyRecords = new(protectors.Count);
        foreach (IFileKeyProtector protector in protectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WrappedFileKeyRecord record = await protector.WrapAsync(dataEncryptionKey, cancellationToken).ConfigureAwait(false);
            ValidateWrappedKeyRecord(record, protector.ProtectionModeId);
            keyRecords.Add(CloneRecord(record));
        }

        byte[] headerBytes = BuildHeaderBytes(originalLength, chunkCount, chunkSize, noncePrefix, keyRecords);
        byte[] headerTag = new byte[HeaderTagSize];
        AuthenticateHeader(dataEncryptionKey, noncePrefix, headerBytes, headerTag);
        byte[] headerHash = ComputeHeaderHash(headerBytes, headerTag);
        return new ContainerWriteHeader(originalLength, chunkCount, chunkSize, headerBytes, headerTag, headerHash);
    }

    internal static async Task EncryptPayloadAsync(
        Stream input,
        Stream output,
        byte[] dataEncryptionKey,
        byte[] noncePrefix,
        byte[] headerHash,
        long originalLength,
        long chunkCount,
        int chunkSize,
        CancellationToken cancellationToken)
    {
        ValidateChunkSize(chunkSize);
        if (CalculateChunkCount(originalLength, chunkSize) != chunkCount)
            throw new InvalidDataException("Declared chunk count does not match the configured chunk size.");

        using AesGcm aes = new(dataEncryptionKey, GcmTagSize);
        byte[] plaintext = new byte[chunkSize];
        long remaining = originalLength;
        try
        {
            for (uint index = 0; index < chunkCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int expected = (int)Math.Min(chunkSize, remaining);
                await ReadExactlyAsync(input, plaintext, expected, cancellationToken).ConfigureAwait(false);

                byte[] ciphertext = new byte[expected];
                byte[] tag = new byte[GcmTagSize];
                byte[] nonce = BuildDataNonce(noncePrefix, index);
                byte[] aad = BuildChunkAad(headerHash, index, expected, originalLength);
                try
                {
                    aes.Encrypt(nonce, plaintext.AsSpan(0, expected), ciphertext, tag, aad);
                    byte[] chunkHeader = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader.AsSpan(0, 4), index);
                    BinaryPrimitives.WriteInt32LittleEndian(chunkHeader.AsSpan(4, 4), expected);
                    await output.WriteAsync(chunkHeader, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                    remaining -= expected;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext.AsSpan(0, expected));
                    CryptographicOperations.ZeroMemory(ciphertext);
                    CryptographicOperations.ZeroMemory(tag);
                    CryptographicOperations.ZeroMemory(nonce);
                    CryptographicOperations.ZeroMemory(aad);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        if (remaining != 0)
            throw new InvalidDataException("The source ended before its declared length was encrypted.");
        if (input.CanSeek && input.Position != input.Length)
            throw new InvalidDataException("The source length changed during encryption.");
    }

    internal static async Task<OpenedContainer> OpenAuthenticatedAsync(
        Stream stream,
        IReadOnlyList<IFileKeyProtector> protectors,
        CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || stream.Length < FixedHeaderSize + HeaderTagSize)
            throw new InvalidDataException("Encrypted container is truncated.");
        if (!TryValidateProtectors(protectors, out string protectorError))
            throw new InvalidDataException(protectorError);

        byte[] prefix = new byte[14];
        await ReadExactlyAsync(stream, prefix, prefix.Length, cancellationToken).ConfigureAwait(false);
        if (!prefix.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Encrypted container magic is invalid.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(8, 2)) != FormatVersion)
            throw new InvalidDataException("Encrypted container version is unsupported.");

        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(10, 4));
        if (headerLength is < FixedHeaderSize or > MaximumHeaderSize || (long)headerLength + HeaderTagSize > stream.Length)
            throw new InvalidDataException("Encrypted container header length is invalid.");

        stream.Position = 0;
        byte[] headerBytes = new byte[headerLength];
        await ReadExactlyAsync(stream, headerBytes, headerBytes.Length, cancellationToken).ConfigureAwait(false);
        byte[] headerTag = new byte[HeaderTagSize];
        await ReadExactlyAsync(stream, headerTag, headerTag.Length, cancellationToken).ConfigureAwait(false);
        ParsedHeader parsed = ParseHeader(headerBytes);

        byte[]? acceptedDek = null;
        bool candidateWasProduced = false;
        foreach (WrappedFileKeyRecord record in parsed.KeyRecords)
        {
            foreach (IFileKeyProtector protector in protectors.Where(p => p.ProtectionModeId == record.ProtectionModeId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? candidate = await protector.TryUnwrapAsync(CloneRecord(record), cancellationToken).ConfigureAwait(false);
                if (candidate is null) continue;
                candidateWasProduced = true;
                if (candidate.Length != DekSize)
                {
                    CryptographicOperations.ZeroMemory(candidate);
                    continue;
                }
                try
                {
                    VerifyHeaderAuthentication(candidate, parsed.NoncePrefix, headerBytes, headerTag);
                    acceptedDek = candidate;
                    break;
                }
                catch (CryptographicException)
                {
                    CryptographicOperations.ZeroMemory(candidate);
                }
            }
            if (acceptedDek is not null) break;
        }

        if (acceptedDek is null)
        {
            if (!candidateWasProduced)
                throw new FileKeyUnavailableException("No available key protector could unlock the encrypted container.");
            throw new CryptographicException("No unwrapped data-encryption key authenticated the container header.");
        }

        return new OpenedContainer(
            parsed.OriginalLength,
            parsed.ChunkCount,
            parsed.ChunkSize,
            parsed.NoncePrefix,
            ComputeHeaderHash(headerBytes, headerTag),
            acceptedDek);
    }

    internal static async Task VerifyOrDecryptPayloadAsync(
        Stream input,
        Stream? plaintextOutput,
        OpenedContainer container,
        CancellationToken cancellationToken)
    {
        using AesGcm aes = new(container.DataEncryptionKey, GcmTagSize);
        long remaining = container.OriginalLength;

        for (uint expectedIndex = 0; expectedIndex < container.ChunkCount; expectedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] chunkHeader = new byte[8];
            await ReadExactlyAsync(input, chunkHeader, chunkHeader.Length, cancellationToken).ConfigureAwait(false);
            uint index = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(0, 4));
            int plaintextLength = BinaryPrimitives.ReadInt32LittleEndian(chunkHeader.AsSpan(4, 4));
            if (index != expectedIndex) throw new InvalidDataException("Encrypted chunk order is invalid.");

            int expectedLength = (int)Math.Min(container.ChunkSize, remaining);
            if (plaintextLength != expectedLength || plaintextLength < 0 || plaintextLength > container.ChunkSize)
                throw new InvalidDataException("Encrypted chunk length is invalid.");

            byte[] ciphertext = new byte[plaintextLength];
            byte[] tag = new byte[GcmTagSize];
            byte[] plaintext = new byte[plaintextLength];
            byte[] nonce = BuildDataNonce(container.NoncePrefix, index);
            byte[] aad = BuildChunkAad(container.HeaderHash, index, plaintextLength, container.OriginalLength);
            try
            {
                await ReadExactlyAsync(input, ciphertext, ciphertext.Length, cancellationToken).ConfigureAwait(false);
                await ReadExactlyAsync(input, tag, tag.Length, cancellationToken).ConfigureAwait(false);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
                if (plaintextOutput is not null)
                    await plaintextOutput.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
                remaining -= plaintextLength;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(aad);
            }
        }

        if (remaining != 0)
            throw new InvalidDataException("Encrypted container ended before the declared plaintext length was satisfied.");
        if (input.CanSeek && input.Position != input.Length)
            throw new InvalidDataException("Encrypted container contains unexpected trailing data.");
    }

    private static byte[] BuildHeaderBytes(
        long originalLength,
        long chunkCount,
        int chunkSize,
        byte[] noncePrefix,
        IReadOnlyList<WrappedFileKeyRecord> keyRecords)
    {
        using MemoryStream recordStream = new();
        using (BinaryWriter recordWriter = new(recordStream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (WrappedFileKeyRecord record in keyRecords)
            {
                recordWriter.Write(record.RecordVersion);
                recordWriter.Write(record.ProtectionModeId);
                recordWriter.Write(record.WrappingAlgorithmId);
                recordWriter.Write(record.Parameters.Length);
                recordWriter.Write(record.Parameters);
                recordWriter.Write(record.WrappedDek.Length);
                recordWriter.Write(record.WrappedDek);
            }
        }

        if (recordStream.Length > MaximumHeaderSize - FixedHeaderSize)
            throw new InvalidDataException("Wrapped key records exceed the version 1 header bound.");
        int headerLength = checked(FixedHeaderSize + (int)recordStream.Length);

        using MemoryStream header = new(headerLength);
        using (BinaryWriter writer = new(header, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(headerLength);
            writer.Write(AlgorithmAes256Gcm);
            writer.Write(chunkSize);
            writer.Write(originalLength);
            writer.Write(chunkCount);
            writer.Write(noncePrefix);
            writer.Write((ushort)keyRecords.Count);
            writer.Write(0u);
            writer.Write(recordStream.ToArray());
        }

        byte[] bytes = header.ToArray();
        if (bytes.Length != headerLength)
            throw new InvalidDataException("Encrypted header serialization length did not match its declaration.");
        return bytes;
    }

    private static ParsedHeader ParseHeader(byte[] headerBytes)
    {
        try
        {
            using MemoryStream stream = new(headerBytes, writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Encrypted container magic is invalid.");
            if (reader.ReadUInt16() != FormatVersion) throw new InvalidDataException("Encrypted container version is unsupported.");
            if (reader.ReadInt32() != headerBytes.Length) throw new InvalidDataException("Encrypted header length is inconsistent.");
            if (reader.ReadUInt16() != AlgorithmAes256Gcm) throw new InvalidDataException("Encrypted container algorithm is unsupported.");

            int chunkSize = reader.ReadInt32();
            ValidateChunkSize(chunkSize);
            long originalLength = reader.ReadInt64();
            long chunkCount = reader.ReadInt64();
            if (originalLength < 0 || chunkCount < 0 || (ulong)chunkCount > uint.MaxValue || CalculateChunkCount(originalLength, chunkSize) != chunkCount)
                throw new InvalidDataException("Encrypted container length metadata is invalid.");

            byte[] noncePrefix = reader.ReadBytes(NoncePrefixSize);
            if (noncePrefix.Length != NoncePrefixSize) throw new InvalidDataException("Encrypted container nonce prefix is truncated.");
            ushort keyCount = reader.ReadUInt16();
            if (keyCount is < 1 or > MaximumKeyRecords) throw new InvalidDataException("Encrypted container key record count is invalid.");
            if (reader.ReadUInt32() != 0) throw new InvalidDataException("Encrypted container contains unsupported required flags.");

            List<WrappedFileKeyRecord> records = new(keyCount);
            HashSet<ushort> seenModes = new();
            for (int i = 0; i < keyCount; i++)
            {
                ushort recordVersion = reader.ReadUInt16();
                ushort protectionMode = reader.ReadUInt16();
                ushort wrappingAlgorithm = reader.ReadUInt16();
                int parameterLength = reader.ReadInt32();
                if (recordVersion != KeyRecordVersion || protectionMode is < 1 or > 4 || wrappingAlgorithm == 0 || parameterLength is < 0 or > MaximumKeyParameters)
                    throw new InvalidDataException("Encrypted container key record metadata is invalid.");
                if (!seenModes.Add(protectionMode)) throw new InvalidDataException("Encrypted container contains duplicate key protection modes.");

                byte[] parameters = reader.ReadBytes(parameterLength);
                if (parameters.Length != parameterLength) throw new InvalidDataException("Encrypted container key parameters are truncated.");
                int wrappedLength = reader.ReadInt32();
                if (wrappedLength is <= 0 or > MaximumWrappedDek) throw new InvalidDataException("Encrypted container wrapped key length is invalid.");
                byte[] wrapped = reader.ReadBytes(wrappedLength);
                if (wrapped.Length != wrappedLength) throw new InvalidDataException("Encrypted container wrapped key is truncated.");
                records.Add(new WrappedFileKeyRecord(recordVersion, protectionMode, wrappingAlgorithm, parameters, wrapped));
            }

            if (stream.Position != headerBytes.Length) throw new InvalidDataException("Encrypted container header contains trailing or ambiguous bytes.");
            return new ParsedHeader(originalLength, chunkCount, chunkSize, noncePrefix, records);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Encrypted container header is truncated.", ex);
        }
    }

    private static void ValidateWrappedKeyRecord(WrappedFileKeyRecord record, ushort expectedMode)
    {
        if (record is null || record.RecordVersion != KeyRecordVersion || record.ProtectionModeId != expectedMode ||
            record.ProtectionModeId is < 1 or > 4 || record.WrappingAlgorithmId == 0 ||
            record.Parameters is null || record.Parameters.Length > MaximumKeyParameters ||
            record.WrappedDek is null || record.WrappedDek.Length is <= 0 or > MaximumWrappedDek)
        {
            throw new InvalidDataException("A key protector returned an invalid wrapped-key record.");
        }
    }

    private static WrappedFileKeyRecord CloneRecord(WrappedFileKeyRecord record) =>
        new(record.RecordVersion, record.ProtectionModeId, record.WrappingAlgorithmId, record.Parameters.ToArray(), record.WrappedDek.ToArray());

    private static void AuthenticateHeader(byte[] dek, byte[] noncePrefix, byte[] headerBytes, byte[] headerTag)
    {
        using AesGcm aes = new(dek, HeaderTagSize);
        byte[] nonce = BuildHeaderNonce(noncePrefix);
        try { aes.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, headerTag, headerBytes); }
        finally { CryptographicOperations.ZeroMemory(nonce); }
    }

    private static void VerifyHeaderAuthentication(byte[] dek, byte[] noncePrefix, byte[] headerBytes, byte[] headerTag)
    {
        using AesGcm aes = new(dek, HeaderTagSize);
        byte[] nonce = BuildHeaderNonce(noncePrefix);
        try { aes.Decrypt(nonce, ReadOnlySpan<byte>.Empty, headerTag, Span<byte>.Empty, headerBytes); }
        finally { CryptographicOperations.ZeroMemory(nonce); }
    }

    private static byte[] ComputeHeaderHash(byte[] headerBytes, byte[] headerTag)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(headerBytes);
        hash.AppendData(headerTag);
        return hash.GetHashAndReset();
    }

    private static byte[] BuildHeaderNonce(byte[] prefix)
    {
        byte[] nonce = new byte[NonceSize];
        prefix.CopyTo(nonce, 0);
        nonce.AsSpan(8, 4).Fill(0xff);
        return nonce;
    }

    private static byte[] BuildDataNonce(byte[] prefix, uint index)
    {
        if (index == uint.MaxValue) throw new InvalidDataException("The reserved header nonce cannot be used for file data.");
        byte[] nonce = new byte[NonceSize];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8, 4), index);
        return nonce;
    }

    private static byte[] BuildChunkAad(byte[] headerHash, uint index, int plaintextLength, long originalLength)
    {
        byte[] aad = new byte[48];
        headerHash.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(32, 4), index);
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(36, 4), plaintextLength);
        BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(40, 8), originalLength);
        return aad;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new InvalidDataException("Encrypted data was truncated unexpectedly.");
            offset += read;
        }
    }

    internal sealed record ContainerWriteHeader(
        long OriginalLength,
        long ChunkCount,
        int ChunkSize,
        byte[] HeaderBytes,
        byte[] HeaderTag,
        byte[] HeaderHash);

    internal sealed class OpenedContainer : IDisposable
    {
        internal OpenedContainer(long originalLength, long chunkCount, int chunkSize, byte[] noncePrefix, byte[] headerHash, byte[] dataEncryptionKey)
        {
            OriginalLength = originalLength;
            ChunkCount = chunkCount;
            ChunkSize = chunkSize;
            NoncePrefix = noncePrefix;
            HeaderHash = headerHash;
            DataEncryptionKey = dataEncryptionKey;
        }

        internal long OriginalLength { get; }
        internal long ChunkCount { get; }
        internal int ChunkSize { get; }
        internal byte[] NoncePrefix { get; }
        internal byte[] HeaderHash { get; }
        internal byte[] DataEncryptionKey { get; }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(DataEncryptionKey);
        }
    }

    private sealed record ParsedHeader(
        long OriginalLength,
        long ChunkCount,
        int ChunkSize,
        byte[] NoncePrefix,
        IReadOnlyList<WrappedFileKeyRecord> KeyRecords);
}

internal sealed class FileKeyUnavailableException : Exception
{
    internal FileKeyUnavailableException(string message) : base(message) { }
}
