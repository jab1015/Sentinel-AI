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
    /// Performs the first production optimization action: conservative cleanup of
    /// stale files from the current user's temporary directory only. The executor
    /// never traverses outside that directory, skips protected/reparse-point files,
    /// caps each run, and verifies recovered free space after execution.
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

            if (!safety.ExecutionAllowed)
            {
                return OptimizationExecutionResult.NotRun(safety.Summary);
            }

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

            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!Directory.Exists(tempRoot))
            {
                return OptimizationExecutionResult.NotRun(
                    "The current user's temporary directory is unavailable.");
            }

            DriveInfo drive = new(Path.GetPathRoot(tempRoot)!);
            long freeBefore = SafeFreeSpace(drive);
            DateTime cutoffUtc = DateTime.UtcNow - MinimumFileAge;

            int examined = 0;
            int deleted = 0;
            int skipped = 0;
            long bytesRequestedForDeletion = 0;
            var errors = new List<string>();

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
                    errors.Add(ex.GetType().Name);
                    return;
                }

                foreach (string path in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (examined >= MaximumFilesPerRun)
                        break;

                    examined++;

                    try
                    {
                        string fullPath = Path.GetFullPath(path);
                        if (!IsUnderRoot(fullPath, tempRoot))
                        {
                            skipped++;
                            continue;
                        }

                        FileInfo file = new(fullPath);
                        FileAttributes attributes = file.Attributes;
                        if ((attributes & (FileAttributes.System | FileAttributes.ReparsePoint)) != 0 ||
                            file.LastWriteTimeUtc > cutoffUtc)
                        {
                            skipped++;
                            continue;
                        }

                        long length = Math.Max(file.Length, 0);
                        file.Delete();
                        bytesRequestedForDeletion += length;
                        deleted++;
                    }
                    catch (IOException)
                    {
                        skipped++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        skipped++;
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        if (errors.Count < 10)
                            errors.Add(ex.GetType().Name);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            drive = new DriveInfo(Path.GetPathRoot(tempRoot)!);
            long freeAfter = SafeFreeSpace(drive);
            long verifiedRecoveredBytes = Math.Max(freeAfter - freeBefore, 0);

            bool verified = !safety.VerificationRequired ||
                            deleted == 0 ||
                            verifiedRecoveredBytes > 0;

            string summary = deleted == 0
                ? $"Sentinel checked {examined} temporary files and found no stale files that were safe to remove."
                : verified
                    ? $"Sentinel safely removed {deleted} stale temporary files and verified {FormatBytes(verifiedRecoveredBytes)} of recovered disk space."
                    : $"Sentinel removed {deleted} stale temporary files, but the expected free-space improvement could not be verified.";

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
                DiagnosticSummary: errors.Count == 0
                    ? "No execution errors were recorded."
                    : $"Skipped errors: {string.Join(", ", errors)}");
        }

        private static bool IsUnderRoot(string path, string root)
        {
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                    Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
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
