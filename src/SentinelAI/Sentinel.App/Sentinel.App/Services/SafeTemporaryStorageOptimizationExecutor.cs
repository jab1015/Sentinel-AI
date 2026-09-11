/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Temporary-file deletion is intentionally disabled until Sentinel has a
    /// handle-based, reparse-point-safe deletion primitive. The previous textual
    /// path checks left a destructive TOCTOU window. Security takes precedence over
    /// reporting an optimization that cannot be performed race-safely.
    /// </summary>
    public sealed class SafeTemporaryStorageOptimizationExecutor
    {
        public Task<OptimizationExecutionResult> ExecuteAsync(
            OptimizationDecision decision,
            OptimizationSafetyAssessment safety,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(decision);
            ArgumentNullException.ThrowIfNull(safety);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(OptimizationExecutionResult.NotRun(
                "Automatic temporary-file cleanup is temporarily unavailable because Sentinel cannot yet guarantee race-resistant deletion through verified file handles. No files were deleted."));
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
