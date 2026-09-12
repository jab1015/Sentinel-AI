using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services.Privacy;

internal sealed class FileEncryptionService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SNTLENC1");
    private const ushort FormatVersion = 1;
    private const ushort AlgorithmAes256Gcm = 1;
    private const int FixedHeaderLength = 50;
    private const int HeaderTagLength = 16;
    private const int AuthenticationTagLength = 16;
    private const int DefaultChunkSize = 1024 * 1024;
    private const int MinimumChunkSize = 64 * 1024;
    private const int MaximumChunkSize = 8 * 1024 * 1024;
    private const int MaximumHeaderLength = 256 * 1024;
    private const int MaximumKeyRecords = 16;
    private const int MaximumKeyRecordSection = 64 * 1024;

    private readonly IFileKeyProtector _keyProtector;
    private readonly int _chunkSize;

    internal FileEncryptionService(IFileKeyProtector keyProtector, int chunkSize = DefaultChunkSize)
    {
        _keyProtector = keyProtector ?? throw new ArgumentNullException(nameof(keyProtector));
        if (chunkSize is < MinimumChunkSize or > MaximumChunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        _chunkSize = chunkSize;
    }

    internal async Task<FileEncryptionResult> EncryptAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateDistinctPaths(sourcePath, destinationPath, out string source, out string destination, out string validationFailure))
            return FileEncryptionResult.Fail(validationFailure, destinationPath);

        if (!File.Exists(source))
            return FileEncryptionResult.Fail("SourceUnavailable", destination);
        if (File.Exists(destination) || Directory.Exists(destination))
            return FileEncryptionResult.Fail("DestinationExists", destination);

        byte[] dataEncryptionKey = RandomNumberGenerator.GetBytes(32);
        byte[] noncePrefix = RandomNumberGenerator.GetBytes(8);
        byte[]? plaintextBuffer = null;
        byte[]? ciphertextBuffer = null;
        bool createdOutput = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileKeyProtectionRecord keyRecord = _keyProtector.Wrap(dataEncryptionKey);
            ValidateKeyRecordForWrite(keyRecord);

            await using FileStream sourceStream = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _chunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            long originalLength = sourceStream.Length;
            ulong chunkCount = CalculateChunkCount(originalLength, _chunkSize);
            if (chunkCount > uint.MaxValue)
                return FileEncryptionResult.Fail("TooManyChunks", destination);

            byte[] headerBytes = BuildHeader(originalLength, chunkCount, noncePrefix, keyRecord, _chunkSize);
            byte[] headerTag = new byte[HeaderTagLength];
            byte[] headerNonce = BuildHeaderNonce(noncePrefix);
            using (AesGcm headerAes = new(dataEncryptionKey, HeaderTagLength))
            {
                headerAes.Encrypt(headerNonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, headerTag, headerBytes);
            }

            byte[] headerHash = SHA256.HashData(Combine(headerBytes, headerTag));
            plaintextBuffer = new byte[_chunkSize];
            ciphertextBuffer = new byte[_chunkSize];

            await using (FileStream output = new(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                _chunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                createdOutput = true;
                await output.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(headerTag, cancellationToken).ConfigureAwait(false);

                long totalPlaintext = 0;
                uint index = 0;
                using AesGcm aes = new(dataEncryptionKey, AuthenticationTagLength);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await ReadChunkAsync(sourceStream, plaintextBuffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    if (index == uint.MaxValue)
                        return FileEncryptionResult.Fail("TooManyChunks", destination);

                    byte[] nonce = BuildChunkNonce(noncePrefix, index);
                    byte[] aad = BuildChunkAad(headerHash, index, read, originalLength);
                    byte[] tag = new byte[AuthenticationTagLength];
                    aes.Encrypt(
                        nonce,
                        plaintextBuffer.AsSpan(0, read),
                        ciphertextBuffer.AsSpan(0, read),
                        tag,
                        aad);

                    byte[] chunkHeader = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader.AsSpan(0, 4), index);
                    BinaryPrimitives.WriteInt32LittleEndian(chunkHeader.AsSpan(4, 4), read);
                    await output.WriteAsync(chunkHeader, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(ciphertextBuffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);

                    totalPlaintext = checked(totalPlaintext + read);
                    index++;
                }

                if (totalPlaintext != originalLength || index != chunkCount)
                    return FileEncryptionResult.Fail("SourceChangedDuringEncryption", destination);

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            FileContainerVerificationResult verification = await VerifyAsync(destination, cancellationToken).ConfigureAwait(false);
            if (!verification.Verified)
            {
                bool partialRemains = !TryDeleteOwnIncompleteOutput(destination);
                return FileEncryptionResult.Fail("PostWriteVerificationFailed:" + verification.Code, destination, partialRemains);
            }

            return new FileEncryptionResult(
                true,
                true,
                false,
                "VerifiedEncryptedCopy",
                destination,
                originalLength);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bool partialRemains = createdOutput && !TryDeleteOwnIncompleteOutput(destination);
            return FileEncryptionResult.Fail("Canceled", destination, partialRemains);
        }
        catch (IOException)
        {
            bool partialRemains = createdOutput && File.Exists(destination) && !TryDeleteOwnIncompleteOutput(destination);
            return FileEncryptionResult.Fail("IoFailure", destination, partialRemains);
        }
        catch (UnauthorizedAccessException)
        {
            bool partialRemains = createdOutput && File.Exists(destination) && !TryDeleteOwnIncompleteOutput(destination);
            return FileEncryptionResult.Fail("AccessDenied", destination, partialRemains);
        }
        catch (CryptographicException)
        {
            bool partialRemains = createdOutput && File.Exists(destination) && !TryDeleteOwnIncompleteOutput(destination);
            return FileEncryptionResult.Fail("CryptographicFailure", destination, partialRemains);
        }
        catch (OverflowException)
        {
            bool partialRemains = createdOutput && File.Exists(destination) && !TryDeleteOwnIncompleteOutput(destination);
            return FileEncryptionResult.Fail("LengthOverflow", destination, partialRemains);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataEncryptionKey);
            CryptographicOperations.ZeroMemory(noncePrefix);
            if (plaintextBuffer is not null) CryptographicOperations.ZeroMemory(plaintextBuffer);
            if (ciphertextBuffer is not null) CryptographicOperations.ZeroMemory(ciphertextBuffer);
        }
    }

    internal async Task<FileContainerVerificationResult> VerifyAsync(
        string encryptedPath,
        CancellationToken cancellationToken = default)
    {
        byte[]? dataEncryptionKey = null;
        byte[]? plaintextBuffer = null;
        byte[]? ciphertextBuffer = null;

        try
        {
            string path = Path.GetFullPath(encryptedPath);
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _chunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            ParsedHeader parsed = await ReadAndValidateHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
            FileKeyProtectionRecord? matching = parsed.KeyRecords
                .Where(record => record.ProtectionModeId == _keyProtector.ProtectionModeId &&
                                 record.WrappingAlgorithmId == _keyProtector.WrappingAlgorithmId)
                .SingleOrDefault();
            if (matching is null)
                return FileContainerVerificationResult.Fail("NoSupportedKeyRecord");

            FileKeyUnwrapResult unwrap = _keyProtector.TryUnwrap(matching);
            if (unwrap.Status != FileKeyUnwrapStatus.Success || unwrap.DataEncryptionKey is null)
                return FileContainerVerificationResult.Fail("KeyUnwrap:" + unwrap.Status);
            dataEncryptionKey = unwrap.DataEncryptionKey;

            byte[] headerNonce = BuildHeaderNonce(parsed.NoncePrefix);
            try
            {
                using AesGcm headerAes = new(dataEncryptionKey, HeaderTagLength);
                headerAes.Decrypt(headerNonce, ReadOnlySpan<byte>.Empty, parsed.HeaderTag, Span<byte>.Empty, parsed.HeaderBytes);
            }
            catch (CryptographicException)
            {
                return FileContainerVerificationResult.Fail("HeaderAuthenticationFailed");
            }

            byte[] headerHash = SHA256.HashData(Combine(parsed.HeaderBytes, parsed.HeaderTag));
            plaintextBuffer = new byte[parsed.ChunkSize];
            ciphertextBuffer = new byte[parsed.ChunkSize];
            long totalPlaintext = 0;
            using AesGcm aes = new(dataEncryptionKey, AuthenticationTagLength);

            for (uint expectedIndex = 0; expectedIndex < parsed.ChunkCount; expectedIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] chunkHeader = new byte[8];
                if (!await ReadExactlyAsync(stream, chunkHeader, cancellationToken).ConfigureAwait(false))
                    return FileContainerVerificationResult.Fail("TruncatedChunkHeader");

                uint index = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(0, 4));
                int plaintextLength = BinaryPrimitives.ReadInt32LittleEndian(chunkHeader.AsSpan(4, 4));
                if (index != expectedIndex)
                    return FileContainerVerificationResult.Fail("UnexpectedChunkIndex");
                if (!IsValidChunkLength(expectedIndex, plaintextLength, parsed))
                    return FileContainerVerificationResult.Fail("InvalidChunkLength");

                if (!await ReadExactlyAsync(stream, ciphertextBuffer.AsMemory(0, plaintextLength), cancellationToken).ConfigureAwait(false))
                    return FileContainerVerificationResult.Fail("TruncatedCiphertext");
                byte[] tag = new byte[AuthenticationTagLength];
                if (!await ReadExactlyAsync(stream, tag, cancellationToken).ConfigureAwait(false))
                    return FileContainerVerificationResult.Fail("TruncatedAuthenticationTag");

                byte[] nonce = BuildChunkNonce(parsed.NoncePrefix, index);
                byte[] aad = BuildChunkAad(headerHash, index, plaintextLength, parsed.OriginalLength);
                try
                {
                    aes.Decrypt(
                        nonce,
                        ciphertextBuffer.AsSpan(0, plaintextLength),
                        tag,
                        plaintextBuffer.AsSpan(0, plaintextLength),
                        aad);
                }
                catch (CryptographicException)
                {
                    return FileContainerVerificationResult.Fail("ChunkAuthenticationFailed");
                }

                totalPlaintext = checked(totalPlaintext + plaintextLength);
            }

            if (totalPlaintext != parsed.OriginalLength)
                return FileContainerVerificationResult.Fail("PlaintextLengthMismatch");
            if (stream.Position != stream.Length)
                return FileContainerVerificationResult.Fail("TrailingData");

            return new FileContainerVerificationResult(true, "Verified", parsed.OriginalLength, parsed.ChunkCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FileContainerVerificationResult.Fail("Canceled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException or OverflowException)
        {
            return FileContainerVerificationResult.Fail("InvalidOrUnavailableContainer");
        }
        finally
        {
            if (dataEncryptionKey is not null) CryptographicOperations.ZeroMemory(dataEncryptionKey);
            if (plaintextBuffer is not null) CryptographicOperations.ZeroMemory(plaintextBuffer);
            if (ciphertextBuffer is not null) CryptographicOperations.ZeroMemory(ciphertextBuffer);
        }
    }

    internal async Task<FileDecryptionResult> DecryptAsync(
        string encryptedPath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateDistinctPaths(encryptedPath, destinationPath, out string source, out string destination, out string validationFailure))
            return FileDecryptionResult.Fail(validationFailure, destinationPath);
        if (!File.Exists(source)) return FileDecryptionResult.Fail("SourceUnavailable", destination);
        if (File.Exists(destination) || Directory.Exists(destination)) return FileDecryptionResult.Fail("DestinationExists", destination);

        FileContainerVerificationResult preflight = await VerifyAsync(source, cancellationToken).ConfigureAwait(false);
        if (!preflight.Verified) return FileDecryptionResult.Fail("VerificationFailed:" + preflight.Code, destination);

        byte[]? dataEncryptionKey = null;
        byte[]? plaintextBuffer = null;
        byte[]? ciphertextBuffer = null;
        bool createdOutput = false;

        try
        {
            await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, _chunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ParsedHeader parsed = await ReadAndValidateHeaderAsync(input, cancellationToken).ConfigureAwait(false);
            FileKeyProtectionRecord? matching = parsed.KeyRecords
                .Where(record => record.ProtectionModeId == _keyProtector.ProtectionModeId &&
                                 record.WrappingAlgorithmId == _keyProtector.WrappingAlgorithmId)
                .SingleOrDefault();
            if (matching is null) return FileDecryptionResult.Fail("NoSupportedKeyRecord", destination);

            FileKeyUnwrapResult unwrap = _keyProtector.TryUnwrap(matching);
            if (unwrap.Status != FileKeyUnwrapStatus.Success || unwrap.DataEncryptionKey is null)
                return FileDecryptionResult.Fail("KeyUnwrap:" + unwrap.Status, destination);
            dataEncryptionKey = unwrap.DataEncryptionKey;

            using (AesGcm headerAes = new(dataEncryptionKey, HeaderTagLength))
            {
                try
                {
                    headerAes.Decrypt(BuildHeaderNonce(parsed.NoncePrefix), ReadOnlySpan<byte>.Empty, parsed.HeaderTag,
                        Span<byte>.Empty, parsed.HeaderBytes);
                }
                catch (CryptographicException)
                {
                    return FileDecryptionResult.Fail("HeaderAuthenticationFailed", destination);
                }
            }

            byte[] headerHash = SHA256.HashData(Combine(parsed.HeaderBytes, parsed.HeaderTag));
            plaintextBuffer = new byte[parsed.ChunkSize];
            ciphertextBuffer = new byte[parsed.ChunkSize];
            long total = 0;

            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, parsed.ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            createdOutput = true;
            using AesGcm aes = new(dataEncryptionKey, AuthenticationTagLength);

            for (uint expectedIndex = 0; expectedIndex < parsed.ChunkCount; expectedIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] chunkHeader = new byte[8];
                if (!await ReadExactlyAsync(input, chunkHeader, cancellationToken).ConfigureAwait(false))
                    return FileDecryptionResult.Fail("TruncatedChunkHeader", destination);
                uint index = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(0, 4));
                int plaintextLength = BinaryPrimitives.ReadInt32LittleEndian(chunkHeader.AsSpan(4, 4));
                if (index != expectedIndex || !IsValidChunkLength(expectedIndex, plaintextLength, parsed))
                    return FileDecryptionResult.Fail("InvalidChunkRecord", destination);
                if (!await ReadExactlyAsync(input, ciphertextBuffer.AsMemory(0, plaintextLength), cancellationToken).ConfigureAwait(false))
                    return FileDecryptionResult.Fail("TruncatedCiphertext", destination);
                byte[] tag = new byte[AuthenticationTagLength];
                if (!await ReadExactlyAsync(input, tag, cancellationToken).ConfigureAwait(false))
                    return FileDecryptionResult.Fail("TruncatedAuthenticationTag", destination);

                try
                {
                    aes.Decrypt(
                        BuildChunkNonce(parsed.NoncePrefix, index),
                        ciphertextBuffer.AsSpan(0, plaintextLength),
                        tag,
                        plaintextBuffer.AsSpan(0, plaintextLength),
                        BuildChunkAad(headerHash, index, plaintextLength, parsed.OriginalLength));
                }
                catch (CryptographicException)
                {
                    return FileDecryptionResult.Fail("ChunkAuthenticationFailed", destination);
                }

                await output.WriteAsync(plaintextBuffer.AsMemory(0, plaintextLength), cancellationToken).ConfigureAwait(false);
                total = checked(total + plaintextLength);
            }

            if (total != parsed.OriginalLength || input.Position != input.Length)
                return FileDecryptionResult.Fail("ContainerLengthMismatch", destination);

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            return new FileDecryptionResult(true, "VerifiedPlaintextCopy", destination, total);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (createdOutput) TryDeleteOwnIncompleteOutput(destination);
            return FileDecryptionResult.Fail("Canceled", destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or OverflowException)
        {
            if (createdOutput) TryDeleteOwnIncompleteOutput(destination);
            return FileDecryptionResult.Fail("DecryptionFailed", destination);
        }
        finally
        {
            if (dataEncryptionKey is not null) CryptographicOperations.ZeroMemory(dataEncryptionKey);
            if (plaintextBuffer is not null) CryptographicOperations.ZeroMemory(plaintextBuffer);
            if (ciphertextBuffer is not null) CryptographicOperations.ZeroMemory(ciphertextBuffer);
        }
    }

    private static byte[] BuildHeader(
        long originalLength,
        ulong chunkCount,
        byte[] noncePrefix,
        FileKeyProtectionRecord keyRecord,
        int chunkSize)
    {
        int recordLength = checked(2 + 2 + 2 + 4 + keyRecord.Parameters.Length + 4 + keyRecord.WrappedDataEncryptionKey.Length);
        int headerLength = checked(FixedHeaderLength + recordLength);
        if (headerLength > MaximumHeaderLength) throw new CryptographicException("Header exceeded the supported limit.");

        byte[] header = new byte[headerLength];
        int offset = 0;
        Magic.CopyTo(header, offset); offset += Magic.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(offset, 2), FormatVersion); offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset, 4), checked((uint)headerLength)); offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(offset, 2), AlgorithmAes256Gcm); offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset, 4), checked((uint)chunkSize)); offset += 4;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(offset, 8), originalLength); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(offset, 8), chunkCount); offset += 8;
        noncePrefix.CopyTo(header, offset); offset += 8;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(offset, 2), 1); offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset, 4), 0); offset += 4;

        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(offset, 2), keyRecord.RecordVersion); offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(offset, 2), keyRecord.ProtectionModeId); offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(offset, 2), keyRecord.WrappingAlgorithmId); offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset, 4), checked((uint)keyRecord.Parameters.Length)); offset += 4;
        keyRecord.Parameters.CopyTo(header, offset); offset += keyRecord.Parameters.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset, 4), checked((uint)keyRecord.WrappedDataEncryptionKey.Length)); offset += 4;
        keyRecord.WrappedDataEncryptionKey.CopyTo(header, offset); offset += keyRecord.WrappedDataEncryptionKey.Length;

        if (offset != headerLength) throw new CryptographicException("Header serialization length mismatch.");
        return header;
    }

    private static async Task<ParsedHeader> ReadAndValidateHeaderAsync(FileStream stream, CancellationToken cancellationToken)
    {
        if (stream.Length < FixedHeaderLength + HeaderTagLength)
            throw new InvalidDataException("Container is truncated.");

        byte[] fixedHeader = new byte[FixedHeaderLength];
        if (!await ReadExactlyAsync(stream, fixedHeader, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Container fixed header is truncated.");
        if (!fixedHeader.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Container magic is invalid.");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(8, 2));
        uint headerLengthRaw = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.AsSpan(10, 4));
        ushort algorithm = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(14, 2));
        uint chunkSizeRaw = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.AsSpan(16, 4));
        long originalLength = BinaryPrimitives.ReadInt64LittleEndian(fixedHeader.AsSpan(20, 8));
        ulong chunkCount = BinaryPrimitives.ReadUInt64LittleEndian(fixedHeader.AsSpan(28, 8));
        byte[] noncePrefix = fixedHeader.AsSpan(36, 8).ToArray();
        ushort keyRecordCount = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(44, 2));
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.AsSpan(46, 4));

        if (version != FormatVersion || algorithm != AlgorithmAes256Gcm || flags != 0)
            throw new InvalidDataException("Container version, algorithm, or flags are unsupported.");
        if (headerLengthRaw < FixedHeaderLength || headerLengthRaw > MaximumHeaderLength)
            throw new InvalidDataException("Container header length is invalid.");
        if (headerLengthRaw + HeaderTagLength > stream.Length)
            throw new InvalidDataException("Container header exceeds the physical file.");
        if (chunkSizeRaw is < MinimumChunkSize or > MaximumChunkSize)
            throw new InvalidDataException("Container chunk size is invalid.");
        if (originalLength < 0 || keyRecordCount is < 1 or > MaximumKeyRecords || chunkCount > uint.MaxValue)
            throw new InvalidDataException("Container length or key-record count is invalid.");

        int chunkSize = checked((int)chunkSizeRaw);
        ulong expectedChunkCount = CalculateChunkCount(originalLength, chunkSize);
        if (chunkCount != expectedChunkCount)
            throw new InvalidDataException("Container chunk count does not match the original length.");

        int headerLength = checked((int)headerLengthRaw);
        byte[] headerBytes = new byte[headerLength];
        fixedHeader.CopyTo(headerBytes, 0);
        if (headerLength > FixedHeaderLength &&
            !await ReadExactlyAsync(stream, headerBytes.AsMemory(FixedHeaderLength), cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Container key records are truncated.");
        }

        List<FileKeyProtectionRecord> records = ParseKeyRecords(headerBytes, keyRecordCount);
        byte[] headerTag = new byte[HeaderTagLength];
        if (!await ReadExactlyAsync(stream, headerTag, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Container header tag is truncated.");

        return new ParsedHeader(headerBytes, headerTag, noncePrefix, chunkSize, originalLength, checked((uint)chunkCount), records);
    }

    private static List<FileKeyProtectionRecord> ParseKeyRecords(byte[] headerBytes, ushort expectedCount)
    {
        List<FileKeyProtectionRecord> records = new(expectedCount);
        int offset = FixedHeaderLength;
        for (int recordIndex = 0; recordIndex < expectedCount; recordIndex++)
        {
            if (offset > headerBytes.Length - 10) throw new InvalidDataException("Key record is truncated.");
            ushort recordVersion = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.AsSpan(offset, 2)); offset += 2;
            ushort protectionMode = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.AsSpan(offset, 2)); offset += 2;
            ushort wrappingAlgorithm = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.AsSpan(offset, 2)); offset += 2;
            uint parameterLengthRaw = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.AsSpan(offset, 4)); offset += 4;
            if (parameterLengthRaw > MaximumKeyRecordSection) throw new InvalidDataException("Key-record parameters are oversized.");
            int parameterLength = checked((int)parameterLengthRaw);
            if (offset > headerBytes.Length - parameterLength - 4) throw new InvalidDataException("Key-record parameters are truncated.");
            byte[] parameters = headerBytes.AsSpan(offset, parameterLength).ToArray(); offset += parameterLength;
            uint wrappedLengthRaw = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.AsSpan(offset, 4)); offset += 4;
            if (wrappedLengthRaw is 0 or > MaximumKeyRecordSection) throw new InvalidDataException("Wrapped DEK length is invalid.");
            int wrappedLength = checked((int)wrappedLengthRaw);
            if (offset > headerBytes.Length - wrappedLength) throw new InvalidDataException("Wrapped DEK is truncated.");
            byte[] wrappedDek = headerBytes.AsSpan(offset, wrappedLength).ToArray(); offset += wrappedLength;
            records.Add(new FileKeyProtectionRecord(recordVersion, protectionMode, wrappingAlgorithm, parameters, wrappedDek));
        }

        if (offset != headerBytes.Length) throw new InvalidDataException("Key-record parsing did not end at HeaderLength.");
        return records;
    }

    private static bool IsValidChunkLength(uint index, int length, ParsedHeader parsed)
    {
        if (length < 0 || length > parsed.ChunkSize) return false;
        if (parsed.ChunkCount == 0) return false;
        long priorBytes = checked((long)index * parsed.ChunkSize);
        long remaining = parsed.OriginalLength - priorBytes;
        if (remaining <= 0) return false;
        int expected = checked((int)Math.Min(parsed.ChunkSize, remaining));
        return length == expected;
    }

    private static void ValidateKeyRecordForWrite(FileKeyProtectionRecord record)
    {
        if (record.Parameters.Length > MaximumKeyRecordSection ||
            record.WrappedDataEncryptionKey.Length is 0 or > MaximumKeyRecordSection)
            throw new CryptographicException("Key-protection record exceeds supported bounds.");
    }

    private static ulong CalculateChunkCount(long originalLength, int chunkSize)
    {
        if (originalLength < 0) throw new InvalidDataException("Negative source length is invalid.");
        if (originalLength == 0) return 0;
        return checked((ulong)(((originalLength - 1) / chunkSize) + 1));
    }

    private static async Task<int> ReadChunkAsync(FileStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0) return false;
            total += read;
        }
        return true;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) =>
        await ReadExactlyAsync(stream, buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

    private static byte[] BuildHeaderNonce(byte[] noncePrefix)
    {
        byte[] nonce = new byte[12];
        noncePrefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8, 4), uint.MaxValue);
        return nonce;
    }

    private static byte[] BuildChunkNonce(byte[] noncePrefix, uint index)
    {
        if (index == uint.MaxValue) throw new CryptographicException("Reserved header nonce index cannot be used for data.");
        byte[] nonce = new byte[12];
        noncePrefix.CopyTo(nonce, 0);
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

    private static byte[] Combine(byte[] left, byte[] right)
    {
        byte[] combined = new byte[checked(left.Length + right.Length)];
        left.CopyTo(combined, 0);
        right.CopyTo(combined, left.Length);
        return combined;
    }

    private static bool TryValidateDistinctPaths(
        string sourcePath,
        string destinationPath,
        out string source,
        out string destination,
        out string failure)
    {
        source = string.Empty;
        destination = string.Empty;
        failure = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            {
                failure = "InvalidPath";
                return false;
            }
            source = Path.GetFullPath(sourcePath);
            destination = Path.GetFullPath(destinationPath);
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            {
                failure = "InPlaceEncryptionForbidden";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failure = "InvalidPath";
            return false;
        }
    }

    private static bool TryDeleteOwnIncompleteOutput(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return !File.Exists(path);
        }
        catch { return false; }
    }

    private sealed record ParsedHeader(
        byte[] HeaderBytes,
        byte[] HeaderTag,
        byte[] NoncePrefix,
        int ChunkSize,
        long OriginalLength,
        uint ChunkCount,
        IReadOnlyList<FileKeyProtectionRecord> KeyRecords);
}

internal sealed record FileEncryptionResult(
    bool Success,
    bool Verified,
    bool PartialArtifactRemains,
    string Code,
    string OutputPath,
    long PlaintextBytes)
{
    internal static FileEncryptionResult Fail(string code, string outputPath, bool partialArtifactRemains = false) =>
        new(false, false, partialArtifactRemains, code, outputPath, 0);
}

internal sealed record FileContainerVerificationResult(bool Verified, string Code, long PlaintextBytes, uint ChunkCount)
{
    internal static FileContainerVerificationResult Fail(string code) => new(false, code, 0, 0);
}

internal sealed record FileDecryptionResult(bool Success, string Code, string OutputPath, long PlaintextBytes)
{
    internal static FileDecryptionResult Fail(string code, string outputPath) => new(false, code, outputPath, 0);
}
