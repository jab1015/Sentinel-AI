/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Provides the verified foundation for quarantine, restore, and permanent
    /// deletion operations. Every system-changing action is gated by
    /// RemediationPolicy and explicit user approval, and success is reported only
    /// after filesystem verification.
    /// </summary>
    public sealed class QuarantineService
    {
        private readonly RemediationPolicy _policy;
        private readonly string _quarantineDirectory;

        public QuarantineService(RemediationPolicy? policy = null, string? quarantineDirectory = null)
        {
            _policy = policy ?? new RemediationPolicy();
            _quarantineDirectory = quarantineDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SentinelAI",
                "Quarantine");
        }

        public async Task<QuarantineResult> QuarantineAsync(
            string sourcePath,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Failed("Sentinel could not verify the file to quarantine.");
            }

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.QuarantineFile,
                RemediationPolicy.RemediationRisk.Moderate,
                hasVerifiedEvidence,
                isWindowsProtectedComponent,
                RequiresElevation: false,
                CanRequestElevation: false));

            if (!decision.Allowed)
            {
                return Failed(decision.Explanation);
            }

            if (decision.RequiresUserApproval && !userApproved)
            {
                return new QuarantineResult(false, true, false, null, null, decision.Explanation);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_quarantineDirectory);

                string originalFullPath = Path.GetFullPath(sourcePath);
                string hash = await ComputeSha256Async(originalFullPath, cancellationToken).ConfigureAwait(false);
                string quarantineName = $"{Guid.NewGuid():N}.sentinel";
                string quarantinePath = Path.Combine(_quarantineDirectory, quarantineName);

                File.Move(originalFullPath, quarantinePath);

                bool verified = File.Exists(quarantinePath) && !File.Exists(originalFullPath);
                if (!verified)
                {
                    return Failed("Sentinel moved the file but could not verify quarantine. No success was reported.");
                }

                return new QuarantineResult(
                    true,
                    false,
                    true,
                    new QuarantineRecord(originalFullPath, quarantinePath, hash, DateTimeOffset.UtcNow),
                    hash,
                    "Sentinel quarantined the approved file and verified that it is no longer in its original location.");
            }
            catch (OperationCanceledException)
            {
                return Failed("The quarantine action was canceled before Sentinel could verify the result.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Failed("Sentinel could not safely quarantine the file. No success was reported.");
            }
        }

        public Task<QuarantineResult> RestoreAsync(
            QuarantineRecord record,
            bool userApproved,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.RestoreQuarantinedFile,
                RemediationPolicy.RemediationRisk.Moderate,
                HasVerifiedEvidence: File.Exists(record.QuarantinePath),
                IsWindowsProtectedComponent: false,
                RequiresElevation: false,
                CanRequestElevation: false));

            if (!decision.Allowed)
            {
                return Task.FromResult(Failed(decision.Explanation));
            }

            if (decision.RequiresUserApproval && !userApproved)
            {
                return Task.FromResult(new QuarantineResult(false, true, false, record, record.Sha256, decision.Explanation));
            }

            return RestoreVerifiedAsync(record, cancellationToken);
        }

        public async Task<QuarantineResult> DeletePermanentlyAsync(
            QuarantineRecord record,
            bool userApproved,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.DeleteQuarantinedFile,
                RemediationPolicy.RemediationRisk.Moderate,
                HasVerifiedEvidence: File.Exists(record.QuarantinePath),
                IsWindowsProtectedComponent: false,
                RequiresElevation: false,
                CanRequestElevation: false));

            if (!decision.Allowed)
            {
                return Failed(decision.Explanation);
            }

            if (decision.RequiresUserApproval && !userApproved)
            {
                return new QuarantineResult(false, true, false, record, record.Sha256, decision.Explanation);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(record.QuarantinePath))
                {
                    return Failed("The quarantined file is no longer available to delete.");
                }

                string currentHash = await ComputeSha256Async(record.QuarantinePath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(currentHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Failed("The quarantined file no longer matches its verified record, so Sentinel will not delete it.");
                }

                File.Delete(record.QuarantinePath);
                bool verified = !File.Exists(record.QuarantinePath);

                return verified
                    ? new QuarantineResult(
                        true,
                        false,
                        true,
                        record,
                        record.Sha256,
                        "Sentinel permanently deleted the approved quarantined file and verified that the isolated copy no longer exists.")
                    : Failed("Sentinel attempted the permanent deletion but could not verify the result.");
            }
            catch (OperationCanceledException)
            {
                return Failed("The permanent deletion was canceled before Sentinel could verify the result.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Failed("Sentinel could not safely delete the quarantined file. No success was reported.");
            }
        }

        private static async Task<QuarantineResult> RestoreVerifiedAsync(
            QuarantineRecord record,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(record.QuarantinePath))
                {
                    return Failed("The quarantined file is no longer available to restore.");
                }

                if (File.Exists(record.OriginalPath))
                {
                    return Failed("Sentinel will not overwrite an existing file at the original location.");
                }

                string currentHash = await ComputeSha256Async(record.QuarantinePath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(currentHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Failed("The quarantined file no longer matches its verified record, so Sentinel will not restore it.");
                }

                string? parent = Path.GetDirectoryName(record.OriginalPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Move(record.QuarantinePath, record.OriginalPath);
                bool verified = File.Exists(record.OriginalPath) && !File.Exists(record.QuarantinePath);

                return verified
                    ? new QuarantineResult(true, false, true, record, record.Sha256,
                        "Sentinel restored the approved file and verified its original location.")
                    : Failed("Sentinel attempted the restore but could not verify the result.");
            }
            catch (OperationCanceledException)
            {
                return Failed("The restore action was canceled before Sentinel could verify the result.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Failed("Sentinel could not safely restore the file. No success was reported.");
            }
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }

        private static QuarantineResult Failed(string message) =>
            new(false, false, false, null, null, message);

        public sealed record QuarantineRecord(
            string OriginalPath,
            string QuarantinePath,
            string Sha256,
            DateTimeOffset QuarantinedAtUtc);

        public sealed record QuarantineResult(
            bool Succeeded,
            bool RequiresUserApproval,
            bool Verified,
            QuarantineRecord? Record,
            string? Sha256,
            string Message);
    }
}
