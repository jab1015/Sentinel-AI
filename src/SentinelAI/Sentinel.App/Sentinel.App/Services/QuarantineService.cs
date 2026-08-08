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

            string originalFullPath = string.Empty;
            string quarantinePath = string.Empty;
            bool moved = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_quarantineDirectory);

                originalFullPath = Path.GetFullPath(sourcePath);
                string sourceHash = await ComputeSha256Async(originalFullPath, cancellationToken)
                    .ConfigureAwait(false);
                string quarantineName = $"{Guid.NewGuid():N}.sentinel";
                quarantinePath = Path.Combine(_quarantineDirectory, quarantineName);

                File.Move(originalFullPath, quarantinePath);
                moved = true;

                string quarantinedHash = await ComputeSha256Async(quarantinePath, cancellationToken)
                    .ConfigureAwait(false);
                bool verified =
                    string.Equals(sourceHash, quarantinedHash, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(quarantinePath) &&
                    !File.Exists(originalFullPath);

                if (!verified)
                {
                    bool rolledBack = TryRollbackMove(quarantinePath, originalFullPath);
                    return Failed(rolledBack
                        ? "The file changed during quarantine verification. Sentinel restored it to the original location and reported no success."
                        : "The file changed during quarantine verification and Sentinel could not verify rollback. User review is required.");
                }

                return new QuarantineResult(
                    true,
                    false,
                    true,
                    new QuarantineRecord(originalFullPath, quarantinePath, quarantinedHash, DateTimeOffset.UtcNow),
                    quarantinedHash,
                    "Sentinel quarantined the approved file and verified its identity and removal from the original location.");
            }
            catch (OperationCanceledException)
            {
                bool rolledBack = !moved || TryRollbackMove(quarantinePath, originalFullPath);
                return Failed(rolledBack
                    ? "The quarantine action was canceled and Sentinel verified that no file remained stranded in quarantine."
                    : "The quarantine action was canceled after the move, and Sentinel could not verify rollback. User review is required.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                bool rolledBack = !moved || TryRollbackMove(quarantinePath, originalFullPath);
                return Failed(rolledBack
                    ? "Sentinel could not safely quarantine the file and verified that no incomplete move remained."
                    : "Sentinel could not complete quarantine or verify rollback of the moved file. User review is required.");
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
                return Failed(decision.Explanation);

            if (decision.RequiresUserApproval && !userApproved)
                return new QuarantineResult(false, true, false, record, record.Sha256, decision.Explanation);

            string deletionPath = record.QuarantinePath + $".delete-{Guid.NewGuid():N}";
            bool isolatedForDeletion = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(record.QuarantinePath))
                    return Failed("The quarantined file is no longer available to delete.");

                File.Move(record.QuarantinePath, deletionPath);
                isolatedForDeletion = true;

                string currentHash = await ComputeSha256Async(deletionPath, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(currentHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    bool rolledBack = TryRollbackMove(deletionPath, record.QuarantinePath);
                    return Failed(rolledBack
                        ? "The isolated file no longer matched its quarantine record. Sentinel restored it to quarantine and did not delete it."
                        : "The isolated file did not match its record and Sentinel could not verify rollback. User review is required.");
                }

                File.Delete(deletionPath);
                bool verified = !File.Exists(deletionPath) && !File.Exists(record.QuarantinePath);
                return verified
                    ? new QuarantineResult(
                        true,
                        false,
                        true,
                        record,
                        record.Sha256,
                        "Sentinel permanently deleted the approved quarantined file and verified that the isolated copy no longer exists.")
                    : Failed("Sentinel attempted permanent deletion but could not verify the result.");
            }
            catch (OperationCanceledException)
            {
                bool rolledBack = !isolatedForDeletion ||
                    TryRollbackMove(deletionPath, record.QuarantinePath);
                return Failed(rolledBack
                    ? "Permanent deletion was canceled and Sentinel restored the file to quarantine."
                    : "Permanent deletion was canceled and Sentinel could not verify restoration to quarantine. User review is required.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                bool rolledBack = !isolatedForDeletion ||
                    TryRollbackMove(deletionPath, record.QuarantinePath);
                return Failed(rolledBack
                    ? "Sentinel could not safely delete the quarantined file and restored its isolated copy."
                    : "Sentinel could not complete deletion or verify restoration of the isolated copy. User review is required.");
            }
        }

        private static async Task<QuarantineResult> RestoreVerifiedAsync(
            QuarantineRecord record,
            CancellationToken cancellationToken)
        {
            bool moved = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(record.QuarantinePath))
                    return Failed("The quarantined file is no longer available to restore.");

                if (File.Exists(record.OriginalPath))
                    return Failed("Sentinel will not overwrite an existing file at the original location.");

                string currentHash = await ComputeSha256Async(record.QuarantinePath, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(currentHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Failed("The quarantined file no longer matches its verified record, so Sentinel will not restore it.");

                string? parent = Path.GetDirectoryName(record.OriginalPath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                File.Move(record.QuarantinePath, record.OriginalPath);
                moved = true;

                string restoredHash = await ComputeSha256Async(record.OriginalPath, cancellationToken)
                    .ConfigureAwait(false);
                bool verified =
                    string.Equals(restoredHash, record.Sha256, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(record.OriginalPath) &&
                    !File.Exists(record.QuarantinePath);

                if (!verified)
                {
                    bool rolledBack = TryRollbackMove(record.OriginalPath, record.QuarantinePath);
                    return Failed(rolledBack
                        ? "The restored file did not match its quarantine record. Sentinel returned it to quarantine and reported no success."
                        : "The restored file did not match its record and Sentinel could not verify rollback. User review is required.");
                }

                return new QuarantineResult(
                    true,
                    false,
                    true,
                    record,
                    record.Sha256,
                    "Sentinel restored the approved file and verified its identity and original location.");
            }
            catch (OperationCanceledException)
            {
                bool rolledBack = !moved ||
                    TryRollbackMove(record.OriginalPath, record.QuarantinePath);
                return Failed(rolledBack
                    ? "Restore was canceled and Sentinel verified that the file remained quarantined."
                    : "Restore was canceled after the move and Sentinel could not verify rollback. User review is required.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                bool rolledBack = !moved ||
                    TryRollbackMove(record.OriginalPath, record.QuarantinePath);
                return Failed(rolledBack
                    ? "Sentinel could not safely restore the file and verified that it remained quarantined."
                    : "Sentinel could not complete restore or verify rollback. User review is required.");
            }
        }

        private static bool TryRollbackMove(string quarantinePath, string originalPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(quarantinePath) ||
                    string.IsNullOrWhiteSpace(originalPath) ||
                    !File.Exists(quarantinePath) ||
                    File.Exists(originalPath))
                {
                    return !File.Exists(quarantinePath) && File.Exists(originalPath);
                }

                File.Move(quarantinePath, originalPath);
                return File.Exists(originalPath) && !File.Exists(quarantinePath);
            }
            catch
            {
                return false;
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
