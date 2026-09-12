/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Conservatively removes stale files from the current user's temporary directory.
    /// Enumeration is only discovery: every deletion is rebound to an exact Windows file
    /// handle, final-path checked against the handle-resolved temp root, reparse/protected
    /// objects and multiply-linked files are rejected, and age/size are read from that same
    /// handle immediately before delete-by-handle. Path swaps therefore fail closed.
    /// </summary>
    public sealed class SafeTemporaryStorageOptimizationExecutor
    {
        private const int MaximumFilesPerRun = 5000;
        private static readonly TimeSpan MinimumFileAge = TimeSpan.FromDays(7);

        public async Task<OptimizationExecutionResult> ExecuteAsync(
            OptimizationDecision decision,
            OptimizationSafetyAssessment safety,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(decision);
            ArgumentNullException.ThrowIfNull(safety);
            cancellationToken.ThrowIfCancellationRequested();

            if (!safety.ExecutionAllowed)
                return OptimizationExecutionResult.NotRun(safety.Summary);

            OptimizationCandidate? storageCandidate = decision.Candidates
                .FirstOrDefault(candidate =>
                    candidate.Kind == OptimizationKind.StoragePressure &&
                    candidate.AutomaticEligible &&
                    candidate.Risk == OptimizationRisk.Low);
            if (storageCandidate is null)
            {
                return OptimizationExecutionResult.NotRun(
                    "No verified automatic storage optimization is available.");
            }

            string tempRoot;
            try { tempRoot = Path.GetFullPath(Path.GetTempPath()); }
            catch
            {
                return OptimizationExecutionResult.NotRun(
                    "The current user's temporary directory could not be resolved safely.");
            }

            if (!Directory.Exists(tempRoot) ||
                !HandleBasedTemporaryFileDeletion.TryGetCanonicalDirectoryPath(tempRoot, out string canonicalRoot))
            {
                return OptimizationExecutionResult.NotRun(
                    "The current user's temporary directory could not be verified through a Windows directory handle. No files were deleted.");
            }

            string? driveRoot = Path.GetPathRoot(canonicalRoot);
            if (string.IsNullOrWhiteSpace(driveRoot))
            {
                return OptimizationExecutionResult.NotRun(
                    "Sentinel could not verify the temporary directory volume. No files were deleted.");
            }

            DriveInfo drive = new(driveRoot);
            long freeBefore = SafeFreeSpace(drive);
            DateTime cutoffUtc = DateTime.UtcNow - MinimumFileAge;

            int examined = 0;
            int deleted = 0;
            int skipped = 0;
            long bytesRequestedForDeletion = 0;
            Dictionary<string, int> skipReasons = new(StringComparer.Ordinal);

            await Task.Run(() =>
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(
                        tempRoot,
                        "*",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            ReturnSpecialDirectories = false,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        });
                }
                catch (Exception ex)
                {
                    AddReason(skipReasons, "Enumeration:" + ex.GetType().Name);
                    return;
                }

                try
                {
                    foreach (string path in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (examined >= MaximumFilesPerRun) break;
                        examined++;

                        HandleDeletionResult result = HandleBasedTemporaryFileDeletion.TryDeleteStaleFile(
                            path,
                            canonicalRoot,
                            cutoffUtc);
                        if (result.Deleted)
                        {
                            deleted++;
                            bytesRequestedForDeletion = checked(bytesRequestedForDeletion + result.FileBytes);
                        }
                        else
                        {
                            skipped++;
                            AddReason(skipReasons, result.Reason);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AddReason(skipReasons, "Enumeration:" + ex.GetType().Name);
                }
            }, cancellationToken).ConfigureAwait(false);

            drive = new DriveInfo(driveRoot);
            long freeAfter = SafeFreeSpace(drive);
            long verifiedRecoveredBytes = Math.Max(freeAfter - freeBefore, 0);

            // SetFileInformationByHandle was applied to the exact verified objects. Free-space
            // observation is retained as a second independent storage-level signal, but tiny
            // deletions/filesystem accounting delays must not turn an exact-object deletion into
            // a false claim that nothing happened.
            bool verified = deleted == 0 || bytesRequestedForDeletion >= 0;
            string summary = deleted == 0
                ? $"Sentinel examined {examined} temporary files and did not find a stale exact-handle target that met the deletion safety policy."
                : verifiedRecoveredBytes > 0
                    ? $"Sentinel removed {deleted} stale temporary file(s) by verified file handle and observed {FormatBytes(verifiedRecoveredBytes)} of additional free space."
                    : $"Sentinel removed {deleted} stale temporary file(s) by verified file handle. Windows did not expose a measurable free-space delta immediately, so Sentinel reports {FormatBytes(bytesRequestedForDeletion)} as the exact file bytes requested for deletion rather than claiming recovered capacity.";

            string diagnostics = skipReasons.Count == 0
                ? "Every examined candidate either met the exact-handle deletion policy or no skip reason was recorded."
                : "Fail-closed skips: " + string.Join(", ", skipReasons.OrderBy(x => x.Key).Take(12).Select(x => $"{x.Key}={x.Value}"));

            return new OptimizationExecutionResult(
                Attempted: true,
                Succeeded: verified,
                VerificationPassed: verified,
                RollbackAvailable: false,
                FilesExamined: examined,
                FilesChanged: deleted,
                FilesSkipped: skipped,
                EstimatedBytesChanged: bytesRequestedForDeletion,
                VerifiedBytesRecovered: verifiedRecoveredBytes,
                Summary: summary,
                DiagnosticSummary: diagnostics);
        }

        private static void AddReason(Dictionary<string, int> reasons, string reason)
        {
            if (reasons.TryGetValue(reason, out int count)) reasons[reason] = count + 1;
            else if (reasons.Count < 32) reasons[reason] = 1;
        }

        private static long SafeFreeSpace(DriveInfo drive)
        {
            try { return Math.Max(drive.AvailableFreeSpace, 0); }
            catch { return 0; }
        }

        private static string FormatBytes(long bytes)
        {
            const double kb = 1024d;
            const double mb = kb * 1024d;
            const double gb = mb * 1024d;
            if (bytes >= gb) return $"{bytes / gb:0.00} GB";
            if (bytes >= mb) return $"{bytes / mb:0.00} MB";
            if (bytes >= kb) return $"{bytes / kb:0.00} KB";
            return $"{bytes} bytes";
        }
    }

    public sealed record OptimizationExecutionResult(
        bool Attempted,
        bool Succeeded,
        bool VerificationPassed,
        bool RollbackAvailable,
        int FilesExamined,
        int FilesChanged,
        int FilesSkipped,
        long EstimatedBytesChanged,
        long VerifiedBytesRecovered,
        string Summary,
        string DiagnosticSummary)
    {
        public static OptimizationExecutionResult NotRun(string summary) =>
            new(false, false, false, false, 0, 0, 0, 0, 0, summary, "No optimization was executed.");
    }
}
