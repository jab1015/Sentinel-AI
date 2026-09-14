using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

/// <summary>
/// User-facing encryption transaction that keeps the plaintext source pinned to the
/// exact filesystem object while the encrypted container is created and verified, then
/// removes that exact plaintext object only after successful authenticated verification.
/// The lower-level FileEncryptionService remains non-destructive for internal callers.
/// </summary>
internal sealed class FileEncryptionReplacementService
{
    private readonly FileEncryptionService _encryption;

    internal FileEncryptionReplacementService(FileEncryptionService? encryption = null)
    {
        _encryption = encryption ?? new FileEncryptionService();
    }

    internal async Task<FileEncryptionResult> EncryptReplacingSourceAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeSource(sourcePath, outputPath, out string source, out string output, out string validationError))
        {
            return FileEncryptionResult.Failure(
                "InvalidPath",
                validationError,
                sourcePath ?? string.Empty,
                outputPath ?? string.Empty);
        }

        try
        {
            using FileStream sourceLease = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (!ExactOwnedOutputCleanup.TryCapture(sourceLease, source, out OwnedFileIdentity sourceIdentity))
            {
                return FileEncryptionResult.Failure(
                    "SourceIdentityUnavailable",
                    "Sentinel could not bind the selected plaintext file to a stable Windows filesystem identity. No encrypted output was accepted and the original file was kept.",
                    source,
                    output);
            }

            FileEncryptionResult encrypted = await _encryption.EncryptAsync(
                source,
                output,
                keyProtectors,
                cancellationToken).ConfigureAwait(false);

            if (!encrypted.Succeeded || !encrypted.Verified)
                return encrypted;

            // Release our source lease only after the encrypted container has been flushed,
            // reopened, and authenticated by FileEncryptionService. The stable identity is
            // retained so a pathname replacement can never be deleted as the source.
            sourceLease.Dispose();

            bool sourceRemoved = ExactOwnedOutputCleanup.TryDeleteSameObject(source, sourceIdentity) &&
                                 !File.Exists(source) &&
                                 !Directory.Exists(source);
            if (!sourceRemoved)
            {
                return encrypted with
                {
                    Succeeded = false,
                    Code = "SourceRetirementFailed",
                    Message = "Sentinel created and authenticated the encrypted file, but Windows did not allow Sentinel to safely remove the exact plaintext source. The encrypted file is valid, but the encryption replacement operation is not complete because the readable original may still be present. Close any program using the original file, then resolve the existing .sentinel.senc copy before trying again."
                };
            }

            return encrypted with
            {
                Code = "VerifiedAndSourceRetired",
                Message = "Sentinel created, flushed, reopened, and authenticated the encrypted container, then removed the exact plaintext source."
            };
        }
        catch (OperationCanceledException)
        {
            return FileEncryptionResult.Failure(
                "Canceled",
                "Encryption was canceled before the plaintext source could be retired. The original file was kept.",
                source,
                output);
        }
        catch (UnauthorizedAccessException)
        {
            return FileEncryptionResult.Failure(
                "AccessDenied",
                "Windows denied access while Sentinel was preparing the verified replacement transaction. The original file was kept.",
                source,
                output);
        }
        catch (IOException ex)
        {
            return FileEncryptionResult.Failure(
                "IoFailure",
                $"Sentinel could not prepare the verified replacement transaction because of an I/O failure ({ex.GetType().Name}). The original file was kept.",
                source,
                output);
        }
        catch (Exception ex)
        {
            return FileEncryptionResult.Failure(
                "UnexpectedFailure",
                $"Sentinel could not complete the verified replacement transaction ({ex.GetType().Name}). The original file was kept.",
                source,
                output);
        }
    }

    private static bool TryNormalizeSource(
        string? sourcePath,
        string? outputPath,
        out string source,
        out string output,
        out string error)
    {
        source = string.Empty;
        output = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(outputPath))
        {
            error = "A fully qualified source and encrypted output path are required.";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(sourcePath) || !Path.IsPathFullyQualified(outputPath))
            {
                error = "The source and encrypted output paths must be fully qualified.";
                return false;
            }

            source = Path.GetFullPath(sourcePath);
            output = Path.GetFullPath(outputPath);
            if (source.Equals(output, StringComparison.OrdinalIgnoreCase))
            {
                error = "Sentinel does not encrypt a plaintext file in place.";
                return false;
            }

            if (!File.Exists(source))
            {
                error = "The plaintext source file does not exist.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The source or encrypted output path is invalid.";
            return false;
        }
    }
}
