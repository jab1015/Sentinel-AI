/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Executes only a storage action selected by StorageOptimizationPlanService,
    /// then performs a fresh Windows analysis to verify the result.
    /// </summary>
    public sealed class StorageOptimizationExecutor
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);
        private readonly StorageOptimizationPlanService _planService = new();

        public async Task<StorageOptimizationExecutionResult> EvaluateAndExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            StorageOptimizationPlan plan =
                await _planService.BuildSystemDrivePlanAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (!plan.ActionWarranted || plan.Action == StorageOptimizationAction.None)
            {
                return new StorageOptimizationExecutionResult(
                    false,
                    false,
                    false,
                    plan.Action,
                    plan.Summary,
                    plan.AnalysisOutput,
                    string.Empty,
                    plan.AnalysisError);
            }

            if (string.IsNullOrWhiteSpace(plan.DefragArguments))
            {
                return new StorageOptimizationExecutionResult(
                    false,
                    false,
                    false,
                    plan.Action,
                    "Sentinel stopped storage optimization because no verified Windows command was available.",
                    plan.AnalysisOutput,
                    string.Empty,
                    string.Empty);
            }

            CommandResult execution = await RunAsync(
                "defrag.exe",
                plan.DefragArguments,
                cancellationToken).ConfigureAwait(false);

            if (execution.ExitCode != 0)
            {
                return new StorageOptimizationExecutionResult(
                    true,
                    false,
                    false,
                    plan.Action,
                    "Windows did not complete the requested storage optimization successfully. Sentinel made no further storage changes.",
                    plan.AnalysisOutput,
                    execution.Output,
                    execution.Error);
            }

            StorageOptimizationPlan verification =
                await _planService.BuildSystemDrivePlanAsync(cancellationToken)
                    .ConfigureAwait(false);

            bool verified = !verification.ActionWarranted;
            string summary = verified
                ? plan.Action == StorageOptimizationAction.Retrim
                    ? "Windows retrim completed and Sentinel verified that no further storage optimization is currently warranted."
                    : "Windows defragmentation completed and Sentinel verified that no further storage optimization is currently warranted."
                : "Windows completed the storage optimization, but Sentinel could not verify that the drive no longer requires optimization. No additional automatic action will be taken now.";

            return new StorageOptimizationExecutionResult(
                true,
                true,
                verified,
                plan.Action,
                summary,
                verification.AnalysisOutput,
                execution.Output,
                execution.Error);
        }

        private static async Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            ProcessExecutionResult execution = await BoundedProcessRunner.RunAsync(
                startInfo,
                CommandTimeout,
                cancellationToken,
                maxOutputChars: 256_000).ConfigureAwait(false);

            if (execution.Outcome == ProcessExecutionOutcome.Canceled && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            string error = execution.Outcome == ProcessExecutionOutcome.TimedOut
                ? $"Windows storage command exceeded the {CommandTimeout.TotalMinutes:0}-minute safety timeout."
                : execution.StandardError;

            return new CommandResult(execution.ExitCode ?? -1, execution.StandardOutput, error);
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record StorageOptimizationExecutionResult(
        bool Attempted,
        bool WindowsReportedSuccess,
        bool Verified,
        StorageOptimizationAction Action,
        string Summary,
        string VerificationOutput,
        string ExecutionOutput,
        string ExecutionError);
}
