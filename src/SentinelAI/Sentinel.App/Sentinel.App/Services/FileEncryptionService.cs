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

internal sealed class FileEncryptionService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SNTLENC1");
    private const ushort FormatVersion = 1;
    private const ushort AlgorithmAes256Gcm = 1;
    private const ushort KeyRecordVersion = 1;
    private const int FixedHeaderSize = 50;
    private const int HeaderTagSize = 16;
    private const int GcmTagSize = 16;
    private const int DekSize = 32;
    private const int NoncePrefixSize = 8;
    private const int NonceSize = 12;
    private const int MinimumChunkSize = 64 * 1024;
    private const int DefaultChunkSize = 1024 * 1024;
    private const int MaximumChunkSize = 8 * 1024 * 1024;
    private const int MaximumHeaderSize = 256 * 1024;
    private const int MaximumKeyRecords = 16;
    private const int MaximumKeyParameters = 64 * 1024;
    private const int MaximumWrappedDek = 64 * 1024;

    private readonly int _chunkSize;

    internal FileEncryptionService(int chunkSize = DefaultChunkSize)
    {
        if (chunkSize is < MinimumChunkSize or > MaximumChunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        _chunkSize = chunkSize;
    }

    internal async Task<FileEncryptionResult> EncryptAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeInputOutput(sourcePath, outputPath, requireSourceFile: true,
            out string source, out string output, out string validationError))
        {
            return FileEncryptionResult.Failure("InvalidPath", validationError, sourcePath ?? string.Empty, outputPath ?? string.Empty);
        }

        if (!TryValidateKeyProtectors(keyProtectors, out string protectorError))
            return FileEncryptionResult.Failure("InvalidKeyProtection", protectorError, source, output);

        if (File.Exists(output) || Directory.Exists(output))
            return FileEncryptionResult.Failure("OutputCollision", "The encrypted output path already exists.", source, output);

        byte[] dek = RandomNumberGenerator.GetBytes(DekSize);
        byte[] noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        long plaintextLength = 0;
        bool outputCreated = false;

        try
        {
            FileAttributes attributes = File.GetAttributes(source);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return FileEncryptionResult.Failure("UnsupportedSource", "Version 1 encryption refuses reparse-point sources until exact-object reparse policy is qualified.", source, output);

            await using FileStream input = new(
                source, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            plaintextLength = input.Length;
            long chunkCount = CalculateChunkCount(plaintextLength, _chunkSize);
            if ((ulong)chunkCount > uint.MaxValue)
                return FileEncryptionResult.Failure("FileTooLarge", "The source requires more chunks than the version 1 nonce space permits.", source, output, plaintextLength);

            List<WrappedFileKeyRecord> keyRecords = new(keyProtectors.Count);
            foreach (IFileKeyProtector protector in keyProtectors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WrappedFileKeyRecord record = await protector.WrapAsync(dek, cancellationToken).ConfigureAwait(false);
                ValidateWrappedKeyRecord(record, protector.ProtectionModeId);
                keyRecords.Add(CloneRecord(record));
            }

            byte[] headerBytes = BuildHeaderBytes(plaintextLength, chunkCount, _chunkSize, noncePrefix, keyRecords);
            byte[] headerTag = new byte[HeaderTagSize];
            AuthenticateHeader(dek, noncePrefix, headerBytes, headerTag);
            byte[] headerHash = ComputeHeaderHash(headerBytes, headerTag);

            string? parent = Path.GetDirectoryName(output);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                return FileEncryptionResult.Failure("OutputDirectoryUnavailable", "The encrypted output directory does not exist.", source, output, plaintextLength);

            await using (FileStream encrypted = new(
                output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                outputCreated = true;
                await encrypted.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
                await encrypted.WriteAsync(headerTag, cancellationToken).ConfigureAwait(false);
                await EncryptChunksAsync(input, encrypted, dek, noncePrefix, headerHash, plaintextLength, chunkCount, cancellationToken).ConfigureAwait(false);
                await encrypted.FlushAsync(cancellationToken).ConfigureAwait(false);
                encrypted.Flush(flushToDisk: true);
            }

            FileContainerVerificationResult verification = await VerifyAsync(output, keyProtectors, cancellationToken).ConfigureAwait(false);
            if (!verification.Succeeded)
            {
                bool remains = !TryDeleteOwnedOutput(output);
                return FileEncryptionResult.Failure(
                    "PostWriteVerificationFailed",
                    $"The encrypted output failed reopen/authentication verification ({verification.Code}). The original file was kept.",
                    source, output, plaintextLength, remains);
            }

            return new FileEncryptionResult(
                true, true, "Verified", "Sentinel created a separate encrypted container, flushed it, reopened it, and authenticated the complete container. The original plaintext was not removed.",
                source, output, plaintextLength, false);
        }
        catch (OperationCanceledException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("Canceled", "Encryption was canceled. The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (IOException ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("IoFailure", $"Encryption stopped safely because Windows reported an I/O failure ({ex.GetType().Name}). The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (UnauthorizedAccessException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("AccessDenied", "Windows denied access during encryption. The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (CryptographicException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("CryptographicFailure", "Authenticated encryption could not complete. The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (InvalidDataException ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("InvalidContainerState", ex.Message, source, output, plaintextLength, remains);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(noncePrefix);
        }
    }

    internal async Task<FileContainerVerificationResult> VerifyAsync(
        string containerPath,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeExistingFile(containerPath, out string container, out string validationError))
            return FileContainerVerificationResult.Failure("InvalidPath", validationError);
        if (!TryValidateKeyProtectors(keyProtectors, out string protectorError))
            return FileContainerVerificationResult.Failure("InvalidKeyProtection", protectorError);

        byte[]? dek = null;
        try
        {
            await using FileStream stream = new(
                container, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            UnlockedHeader header = await ReadAndUnlockHeaderAsync(stream, keyProtectors, cancellationToken).ConfigureAwait(false);
            dek = header.DataEncryptionKey;
            await VerifyOrDecryptChunksAsync(stream, output: null, header, cancellationToken).ConfigureAwait(false);
            return new FileContainerVerificationResult(true, "Verified", "The complete encrypted container authenticated successfully.", header.OriginalLength, header.ChunkCount, header.ChunkSize);
        }
        catch (OperationCanceledException)
        {
            return FileContainerVerificationResult.Failure("Canceled", "Container verification was canceled.");
        }
        catch (InvalidDataException ex)
        {
            return FileContainerVerificationResult.Failure("InvalidContainer", ex.Message);
        }
        catch (CryptographicException)
        {
            return FileContainerVerificationResult.Failure("AuthenticationFailed", "The encrypted container failed authenticated verification.");
        }
        catch (IOException ex)
        {
            return FileContainerVerificationResult.Failure("IoFailure", $"The encrypted container could not be read safely ({ex.GetType().Name}).");
        }
        catch (UnauthorizedAccessException)
        {
            return FileContainerVerificationResult.Failure("AccessDenied", "Windows denied access to the encrypted container.");
        }
        finally
        {
            if (dek is not null) CryptographicOperations.ZeroMemory(dek);
        }
    }

    internal async Task<FileDecryptionResult> DecryptAsync(
        string containerPath,
        string outputPath,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeInputOutput(containerPath, outputPath, requireSourceFile: true,
            out string container, out string output, out string validationError))
        {
            return FileDecryptionResult.Failure("InvalidPath", validationError, containerPath ?? string.Empty, outputPath ?? string.Empty);
        }
        if (!TryValidateKeyProtectors(keyProtectors, out string protectorError))
            return FileDecryptionResult.Failure("InvalidKeyProtection", protectorError, container, output);
        if (File.Exists(output) || Directory.Exists(output))
            return FileDecryptionResult.Failure("OutputCollision", "The plaintext output path already exists.", container, output);

        byte[]? dek = null;
        bool outputCreated = false;
        try
        {
            await using FileStream input = new(
                container, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            UnlockedHeader header = await ReadAndUnlockHeaderAsync(input, keyProtectors, cancellationToken).ConfigureAwait(false);
            dek = header.DataEncryptionKey;

            string? parent = Path.GetDirectoryName(output);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                return FileDecryptionResult.Failure("OutputDirectoryUnavailable", "The plaintext output directory does not exist.", container, output);

            await using (FileStream plaintext = new(
                output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                outputCreated = true;
                await VerifyOrDecryptChunksAsync(input, plaintext, header, cancellationToken).ConfigureAwait(false);
                await plaintext.FlushAsync(cancellationToken).ConfigureAwait(false);
                plaintext.Flush(flushToDisk: true);
            }

            return new FileDecryptionResult(true, "Verified", "The container authenticated and was decrypted to a separate output file.", container, output, header.OriginalLength, false);
        }
        catch (OperationCanceledException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("Canceled", "Decryption was canceled; no plaintext output was accepted.", container, output, remains);
        }
        catch (InvalidDataException ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("InvalidContainer", ex.Message, container, output, remains);
        }
        catch (CryptographicException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("AuthenticationFailed", "The container failed authenticated decryption; no plaintext output was accepted.", container, output, remains);
        }
        catch (IOException ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("IoFailure", $"Decryption stopped safely after an I/O failure ({ex.GetType().Name}).", container, output, remains);
        }
        catch (UnauthorizedAccessException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("AccessDenied", "Windows denied access during decryption.", container, output, remains);
        }
        finally
        {
            if (dek is not null) CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static async Task EncryptChunksAsync(
        FileStream input,
        FileStream output,
        byte[] dek,
        byte[] noncePrefix,
        byte[] headerHash,
        long originalLength,
        long chunkCount,
        CancellationToken cancellationToken)
    {
        using AesGcm aes = new(dek, GcmTagSize);
        byte[] plaintext = new byte[Math.Min(DefaultChunkSize, (int)Math.Min(originalLength == 0 ? DefaultChunkSize : originalLength, int.MaxValue))];
        int configuredChunkSize = plaintext.Length;
        if (chunkCount > 0)
        {
            configuredChunkSize = (int)Math.Min(int.MaxValue, (originalLength + chunkCount - 1) / chunkCount);
            plaintext = new byte[configuredChunkSize];
        }

        long remaining = originalLength;
        for (uint index = 0; index < chunkCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int expected = (int)Math.Min(configuredChunkSize, remaining);
            await ReadExactlyAsync(input, plaintext, expected, cancellationToken).ConfigureAwait(false);

            byte[] ciphertext = new byte[expected];
            byte[] tag = new byte[GcmTagSize];
            byte[] nonce = BuildNonce(noncePrefix, index);
            byte[] aad = BuildChunkAad(headerHash, index, expected, originalLength);
            aes.Encrypt(nonce, plaintext.AsSpan(0, expected), ciphertext, tag, aad);

            byte[] chunkHeader = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader.AsSpan(0, 4), index);
            BinaryPrimitives.WriteInt32LittleEndian(chunkHeader.AsSpan(4, 4), expected);
            await output.WriteAsync(chunkHeader, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
            remaining -= expected;

            CryptographicOperations.ZeroMemory(plaintext.AsSpan(0, expected));
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(aad);
        }

        if (remaining != 0 || input.Position != input.Length)
            throw new InvalidDataException("The source file length changed during encryption.");
        CryptographicOperations.ZeroMemory(plaintext);
    }

    private static async Task VerifyOrDecryptChunksAsync(
        FileStream input,
        FileStream? output,
        UnlockedHeader header,
        CancellationToken cancellationToken)
    {
        using AesGcm aes = new(header.DataEncryptionKey, GcmTagSize);
        long remaining = header.OriginalLength;

        for (uint expectedIndex = 0; expectedIndex < header.ChunkCount; expectedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] chunkHeader = new byte[8];
            await ReadExactlyAsync(input, chunkHeader, chunkHeader.Length, cancellationToken).ConfigureAwait(false);
            uint index = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(0, 4));
            int plaintextLength = BinaryPrimitives.ReadInt32LittleEndian(chunkHeader.AsSpan(4, 4));
            if (index != expectedIndex)
                throw new InvalidDataException("Encrypted chunk order is invalid.");
            int expectedLength = (int)Math.Min(header.ChunkSize, remaining);
            if (plaintextLength != expectedLength || plaintextLength < 0 || plaintextLength > header.ChunkSize)
                throw new InvalidDataException("Encrypted chunk length is invalid.");

            byte[] ciphertext = new byte[plaintextLength];
            byte[] tag = new byte[GcmTagSize];
            await ReadExactlyAsync(input, ciphertext, ciphertext.Length, cancellationToken).ConfigureAwait(false);
            await ReadExactlyAsync(input, tag, tag.Length, cancellationToken).ConfigureAwait(false);

            byte[] plaintext = new byte[plaintextLength];
            byte[] nonce = BuildNonce(header.NoncePrefix, index);
            byte[] aad = BuildChunkAad(header.HeaderHash, index, plaintextLength, header.OriginalLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            if (output is not null)
                await output.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);

            remaining -= plaintextLength;
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(aad);
        }

        if (remaining != 0)
            throw new InvalidDataException("Encrypted container ended before the declared plaintext length was satisfied.");
        if (input.Position != input.Length)
            throw new InvalidDataException("Encrypted container contains unexpected trailing data.");
    }

    private static async Task<UnlockedHeader> ReadAndUnlockHeaderAsync(
        FileStream stream,
        IReadOnlyList<IFileKeyProtector> protectors,
        CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || stream.Length < FixedHeaderSize + HeaderTagSize)
            throw new InvalidDataException("Encrypted container is truncated.");

        byte[] prefix = new byte[14];
        await ReadExactlyAsync(stream, prefix, prefix.Length, cancellationToken).ConfigureAwait(false);
        if (!prefix.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Encrypted container magic is invalid.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(8, 2));
        if (version != FormatVersion)
            throw new InvalidDataException("Encrypted container version is unsupported.");
        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(10, 4));
        if (headerLength is < FixedHeaderSize or > MaximumHeaderSize || headerLength + HeaderTagSize > stream.Length)
            throw new InvalidDataException("Encrypted container header length is invalid.");

        stream.Position = 0;
        byte[] headerBytes = new byte[headerLength];
        await ReadExactlyAsync(stream, headerBytes, headerBytes.Length, cancellationToken).ConfigureAwait(false);
        byte[] headerTag = new byte[HeaderTagSize];
        await ReadExactlyAsync(stream, headerTag, headerTag.Length, cancellationToken).ConfigureAwait(false);

        ParsedHeader parsed = ParseHeader(headerBytes);
        byte[]? acceptedDek = null;
        bool anyDekCandidate = false;

        foreach (WrappedFileKeyRecord record in parsed.KeyRecords)
        {
            foreach (IFileKeyProtector protector in protectors.Where(p => p.ProtectionModeId == record.ProtectionModeId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? candidate = await protector.TryUnwrapAsync(CloneRecord(record), cancellationToken).ConfigureAwait(false);
                if (candidate is null) continue;
                anyDekCandidate = true;
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
            throw anyDekCandidate
                ? new CryptographicException("No available key record authenticated the container header.")
                : new InvalidDataException("No available key protector could unlock the encrypted container.");

        return new UnlockedHeader(
            parsed.OriginalLength,
            parsed.ChunkCount,
            parsed.ChunkSize,
            parsed.NoncePrefix,
            ComputeHeaderHash(headerBytes, headerTag),
            acceptedDek);
    }

    private static byte[] BuildHeaderBytes(
        long originalLength,
        long chunkCount,
        int chunkSize,
        byte[] noncePrefix,
        IReadOnlyList<WrappedFileKeyRecord> keyRecords)
    {
        using MemoryStream recordsBuffer = new();
        using (BinaryWriter recordWriter = new(recordsBuffer, Encoding.UTF8, leaveOpen: true))
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

        if (recordsBuffer.Length > MaximumHeaderSize - FixedHeaderSize)
            throw new InvalidDataException("Wrapped key records exceed the version 1 header bound.");
        int headerLength = checked(FixedHeaderSize + (int)recordsBuffer.Length);

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
            writer.Write(recordsBuffer.ToArray());
        }

        byte[] bytes = header.ToArray();
        if (bytes.Length != headerLength)
            throw new InvalidDataException("Encrypted header serialization length did not match its declaration.");
        return bytes;
    }

    private static ParsedHeader ParseHeader(byte[] headerBytes)
    {
        using MemoryStream stream = new(headerBytes, writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        byte[] magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Encrypted container magic is invalid.");
        if (reader.ReadUInt16() != FormatVersion) throw new InvalidDataException("Encrypted container version is unsupported.");
        int declaredHeaderLength = reader.ReadInt32();
        if (declaredHeaderLength != headerBytes.Length) throw new InvalidDataException("Encrypted header length is inconsistent.");
        if (reader.ReadUInt16() != AlgorithmAes256Gcm) throw new InvalidDataException("Encrypted container algorithm is unsupported.");

        int chunkSize = reader.ReadInt32();
        if (chunkSize is < MinimumChunkSize or > MaximumChunkSize) throw new InvalidDataException("Encrypted container chunk size is invalid.");
        long originalLength = reader.ReadInt64();
        long chunkCount = reader.ReadInt64();
        if (originalLength < 0 || chunkCount < 0 || (ulong)chunkCount > uint.MaxValue)
            throw new InvalidDataException("Encrypted container length metadata is invalid.");
        if (CalculateChunkCount(originalLength, chunkSize) != chunkCount)
            throw new InvalidDataException("Encrypted container chunk count is inconsistent with its plaintext length.");

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
            if (recordVersion != KeyRecordVersion || protectionMode is < 1 or > 4 || wrappingAlgorithm == 0 ||
                parameterLength is < 0 or > MaximumKeyParameters)
            {
                throw new InvalidDataException("Encrypted container key record metadata is invalid.");
            }
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

    private static void AuthenticateHeader(byte[] dek, byte[] noncePrefix, byte[] headerBytes, byte[] headerTag)
    {
        using AesGcm aes = new(dek, HeaderTagSize);
        byte[] nonce = BuildHeaderNonce(noncePrefix);
        aes.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, headerTag, headerBytes);
        CryptographicOperations.ZeroMemory(nonce);
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
        nonce[8] = 0xff;
        nonce[9] = 0xff;
        nonce[10] = 0xff;
        nonce[11] = 0xff;
        return nonce;
    }

    private static byte[] BuildNonce(byte[] prefix, uint index)
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

    private static long CalculateChunkCount(long length, int chunkSize) =>
        length == 0 ? 0 : (length / chunkSize) + (length % chunkSize == 0 ? 0 : 1);

    private static bool TryValidateKeyProtectors(IReadOnlyList<IFileKeyProtector>? protectors, out string error)
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
        new(record.RecordVersion, record.ProtectionModeId, record.WrappingAlgorithmId,
            record.Parameters.ToArray(), record.WrappedDek.ToArray());

    private static bool TryNormalizeExistingFile(string? path, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "A file path is required.";
            return false;
        }
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                error = "The file path must be fully qualified.";
                return false;
            }
            fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                error = "The source file does not exist.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The file path is invalid.";
            return false;
        }
    }

    private static bool TryNormalizeInputOutput(
        string? sourcePath,
        string? outputPath,
        bool requireSourceFile,
        out string source,
        out string output,
        out string error)
    {
        source = string.Empty;
        output = string.Empty;
        error = string.Empty;
        if (!TryNormalizeExistingFile(sourcePath, out source, out error) && requireSourceFile) return false;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "An output path is required.";
            return false;
        }
        try
        {
            if (!Path.IsPathFullyQualified(outputPath))
            {
                error = "The output path must be fully qualified.";
                return false;
            }
            output = Path.GetFullPath(outputPath);
            if (source.Equals(output, StringComparison.OrdinalIgnoreCase))
            {
                error = "Version 1 encryption/decryption does not operate in place.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The output path is invalid.";
            return false;
        }
    }

    private static bool TryDeleteOwnedOutput(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return !File.Exists(path);
        }
        catch { return false; }
    }

    private sealed record ParsedHeader(
        long OriginalLength,
        long ChunkCount,
        int ChunkSize,
        byte[] NoncePrefix,
        IReadOnlyList<WrappedFileKeyRecord> KeyRecords);

    private sealed record UnlockedHeader(
        long OriginalLength,
        long ChunkCount,
        int ChunkSize,
        byte[] NoncePrefix,
        byte[] HeaderHash,
        byte[] DataEncryptionKey);
}
