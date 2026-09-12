using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class FileEncryptionService
{
    private const int DekSize = 32;
    private const int NoncePrefixSize = 8;
    private readonly int _chunkSize;

    internal FileEncryptionService(int chunkSize = SentinelEncryptedContainerV1.DefaultChunkSize)
    {
        SentinelEncryptedContainerV1.ValidateChunkSize(chunkSize);
        _chunkSize = chunkSize;
    }

    internal async Task<FileEncryptionResult> EncryptAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeInputOutput(sourcePath, outputPath, out string source, out string output, out string validationError))
            return FileEncryptionResult.Failure("InvalidPath", validationError, sourcePath ?? string.Empty, outputPath ?? string.Empty);
        if (!SentinelEncryptedContainerV1.TryValidateProtectors(keyProtectors, out string protectorError))
            return FileEncryptionResult.Failure("InvalidKeyProtection", protectorError, source, output);
        if (File.Exists(output) || Directory.Exists(output))
            return FileEncryptionResult.Failure("OutputCollision", "The encrypted output path already exists.", source, output);

        string? parent = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return FileEncryptionResult.Failure("OutputDirectoryUnavailable", "The encrypted output directory does not exist.", source, output);

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
            SentinelEncryptedContainerV1.ContainerWriteHeader header = await SentinelEncryptedContainerV1.CreateHeaderAsync(
                plaintextLength, _chunkSize, keyProtectors, dek, noncePrefix, cancellationToken).ConfigureAwait(false);

            await using (FileStream encrypted = new(
                output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                outputCreated = true;
                await encrypted.WriteAsync(header.HeaderBytes, cancellationToken).ConfigureAwait(false);
                await encrypted.WriteAsync(header.HeaderTag, cancellationToken).ConfigureAwait(false);
                await SentinelEncryptedContainerV1.EncryptPayloadAsync(
                    input, encrypted, dek, noncePrefix, header.HeaderHash,
                    plaintextLength, header.ChunkCount, header.ChunkSize, cancellationToken).ConfigureAwait(false);
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
                true, true, "Verified",
                "Sentinel created a separate encrypted container, flushed it, reopened it, and authenticated the complete container. The original plaintext was not removed.",
                source, output, plaintextLength, false);
        }
        catch (OperationCanceledException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("Canceled", "Encryption was canceled. The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (UnauthorizedAccessException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("AccessDenied", "Windows denied access during encryption. The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (IOException ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("IoFailure", $"Encryption stopped safely after an I/O failure ({ex.GetType().Name}). The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (CryptographicException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("CryptographicFailure", "Authenticated encryption or key protection could not complete. The original file was kept.", source, output, plaintextLength, remains);
        }
        catch (InvalidDataException ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("InvalidContainerState", ex.Message, source, output, plaintextLength, remains);
        }
        catch (Exception ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileEncryptionResult.Failure("UnexpectedFailure", $"Encryption stopped safely ({ex.GetType().Name}). The original file was kept.", source, output, plaintextLength, remains);
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
        if (!SentinelEncryptedContainerV1.TryValidateProtectors(keyProtectors, out string protectorError))
            return FileContainerVerificationResult.Failure("InvalidKeyProtection", protectorError);

        try
        {
            await using FileStream stream = new(
                container, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using SentinelEncryptedContainerV1.OpenedContainer opened = await SentinelEncryptedContainerV1.OpenAuthenticatedAsync(
                stream, keyProtectors, cancellationToken).ConfigureAwait(false);
            await SentinelEncryptedContainerV1.VerifyOrDecryptPayloadAsync(stream, null, opened, cancellationToken).ConfigureAwait(false);
            return new FileContainerVerificationResult(true, "Verified", "The complete encrypted container authenticated successfully.", opened.OriginalLength, opened.ChunkCount, opened.ChunkSize);
        }
        catch (OperationCanceledException)
        {
            return FileContainerVerificationResult.Failure("Canceled", "Container verification was canceled.");
        }
        catch (FileKeyUnavailableException)
        {
            return FileContainerVerificationResult.Failure("KeyUnavailable", "None of the supplied key protectors could unlock this container.");
        }
        catch (InvalidDataException ex)
        {
            return FileContainerVerificationResult.Failure("InvalidContainer", ex.Message);
        }
        catch (CryptographicException)
        {
            return FileContainerVerificationResult.Failure("AuthenticationFailed", "The encrypted container failed authenticated verification.");
        }
        catch (UnauthorizedAccessException)
        {
            return FileContainerVerificationResult.Failure("AccessDenied", "Windows denied access to the encrypted container.");
        }
        catch (IOException ex)
        {
            return FileContainerVerificationResult.Failure("IoFailure", $"The encrypted container could not be read safely ({ex.GetType().Name}).");
        }
        catch (Exception ex)
        {
            return FileContainerVerificationResult.Failure("UnexpectedFailure", $"Container verification stopped safely ({ex.GetType().Name}).");
        }
    }

    internal async Task<FileDecryptionResult> DecryptAsync(
        string containerPath,
        string outputPath,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeInputOutput(containerPath, outputPath, out string container, out string output, out string validationError))
            return FileDecryptionResult.Failure("InvalidPath", validationError, containerPath ?? string.Empty, outputPath ?? string.Empty);
        if (!SentinelEncryptedContainerV1.TryValidateProtectors(keyProtectors, out string protectorError))
            return FileDecryptionResult.Failure("InvalidKeyProtection", protectorError, container, output);
        if (File.Exists(output) || Directory.Exists(output))
            return FileDecryptionResult.Failure("OutputCollision", "The plaintext output path already exists.", container, output);

        string? parent = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return FileDecryptionResult.Failure("OutputDirectoryUnavailable", "The plaintext output directory does not exist.", container, output);

        bool outputCreated = false;
        try
        {
            await using FileStream input = new(
                container, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using SentinelEncryptedContainerV1.OpenedContainer opened = await SentinelEncryptedContainerV1.OpenAuthenticatedAsync(
                input, keyProtectors, cancellationToken).ConfigureAwait(false);

            await using (FileStream plaintext = new(
                output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                outputCreated = true;
                await SentinelEncryptedContainerV1.VerifyOrDecryptPayloadAsync(input, plaintext, opened, cancellationToken).ConfigureAwait(false);
                await plaintext.FlushAsync(cancellationToken).ConfigureAwait(false);
                plaintext.Flush(flushToDisk: true);
            }

            return new FileDecryptionResult(true, "Verified", "The container authenticated and was decrypted to a separate output file.", container, output, opened.OriginalLength, false);
        }
        catch (OperationCanceledException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("Canceled", "Decryption was canceled; no plaintext output was accepted.", container, output, remains);
        }
        catch (FileKeyUnavailableException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("KeyUnavailable", "None of the supplied key protectors could unlock this container.", container, output, remains);
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
        catch (UnauthorizedAccessException)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("AccessDenied", "Windows denied access during decryption.", container, output, remains);
        }
        catch (IOException ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("IoFailure", $"Decryption stopped safely after an I/O failure ({ex.GetType().Name}).", container, output, remains);
        }
        catch (Exception ex)
        {
            bool remains = outputCreated && !TryDeleteOwnedOutput(output);
            return FileDecryptionResult.Failure("UnexpectedFailure", $"Decryption stopped safely ({ex.GetType().Name}).", container, output, remains);
        }
    }

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
        out string source,
        out string output,
        out string error)
    {
        output = string.Empty;
        if (!TryNormalizeExistingFile(sourcePath, out source, out error)) return false;
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
}
