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
    /// Executes only a Windows integrity repair that has passed Sentinel's planning
    /// and safety layers. The condition is revalidated immediately before execution
    /// and verified afterward with a fresh read-only integrity assessment.
    /// </summary>
    public sealed class SystemImageRepairExecutor
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(60);
        private readonly SystemImageRepairPlanService _planService = new();
        private readonly SystemImageRepairSafetyService _safetyService = new();
        private readonly SystemImageHealthAssessmentService _assessmentService = new();

        public async Task<SystemImageRepairExecutionResult> EvaluateAndExecuteAsync(
            OptimizationSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            SystemImageRepairPlan plan =
                await _planService.BuildPlanAsync(cancellationToken).ConfigureAwait(false);

            SystemImageRepairSafetyAssessment safety = _safetyService.Evaluate(plan, settings);
            if (!safety.ExecutionAllowed || safety.ApprovedCandidate is null)
                return SystemImageRepairExecutionResult.NotRun(safety.Summary);

            SystemImageRepairCandidate candidate = safety.ApprovedCandidate;

            // Revalidate immediately before changing Windows. If corruption is no
            // longer present, Sentinel must not run a repair unnecessarily.
            SystemImageHealthAssessment before =
                await _assessmentService.AssessAsync(cancellationToken).ConfigureAwait(false);

            bool stillWarranted = candidate.Action switch
            {
                SystemImageRepairAction.RestoreComponentStore => before.ComponentStoreCorruptionDetected,
                SystemImageRepairAction.RepairProtectedFiles =>
                    before.ProtectedFilesCorruptionDetected && !before.ComponentStoreCorruptionDetected,
                _ => false
            };

            if (!stillWarranted)
            {
                return new SystemImageRepairExecutionResult(
                    false,
                    true,
                    true,
                    candidate.Action,
                    "The Windows integrity condition recovered or no longer matches the approved repair. Sentinel made no change.",
                    string.Empty,
                    string.Empty);
            }

            string fileName;
            string arguments;

            if (candidate.Action == SystemImageRepairAction.RestoreComponentStore)
            {
                fileName = "dism.exe";
                arguments = "/Online /Cleanup-Image /RestoreHealth";
            }
            else if (candidate.Action == SystemImageRepairAction.RepairProtectedFiles)
            {
                fileName = "sfc.exe";
                arguments = "/scannow";
            }
            else
            {
                return SystemImageRepairExecutionResult.NotRun(
                    "The approved Windows integrity action is not supported by the automatic repair executor.");
            }

            CommandResult execution = await RunAsync(
                fileName,
                arguments,
                cancellationToken).ConfigureAwait(false);

            if (execution.ExitCode != 0)
            {
                bool elevationRequired =
                    execution.Output.Contains("elevated permissions", StringComparison.OrdinalIgnoreCase) ||
                    execution.Error.Contains("elevated permissions", StringComparison.OrdinalIgnoreCase) ||
                    execution.Output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    execution.Error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);

                return new SystemImageRepairExecutionResult(
                    true,
                    false,
                    false,
                    candidate.Action,
                    elevationRequired
                        ? "Sentinel verified the Windows integrity repair, but Windows requires elevated permission to complete it. No additional automatic repair was attempted."
                        : "Windows did not complete the verified integrity repair successfully. Sentinel stopped without attempting a different repair.",
                    execution.Output,
                    execution.Error);
            }

            if (!safety.VerificationRequired)
            {
                return new SystemImageRepairExecutionResult(
                    true,
                    true,
                    false,
                    candidate.Action,
                    "Windows completed the integrity repair.",
                    execution.Output,
                    execution.Error);
            }

            SystemImageHealthAssessment after =
                await _assessmentService.AssessAsync(cancellationToken).ConfigureAwait(false);

            bool verified = candidate.Action switch
            {
                SystemImageRepairAction.RestoreComponentStore => !after.ComponentStoreCorruptionDetected,
                SystemImageRepairAction.RepairProtectedFiles => !after.ProtectedFilesCorruptionDetected,
                _ => false
            };

            string summary = verified
                ? candidate.Action == SystemImageRepairAction.RestoreComponentStore
                    ? "Sentinel repaired the Windows component store and verified that repairable component-store corruption is no longer detected."
                    : "Sentinel repaired protected Windows files and verified that protected-file integrity violations are no longer detected."
                : "Windows completed the integrity repair command, but Sentinel could not verify that the corruption condition was cleared. No further automatic integrity repair will run in this cycle.";

            return new SystemImageRepairExecutionResult(
                true,
                true,
                verified,
                candidate.Action,
                summary,
                execution.Output,
                execution.Error);
        }

        private static async Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeoutSource = new(CommandTimeout);
            using CancellationTokenSource linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }

                return new CommandResult(
                    -1,
                    string.Empty,
                    $"Windows integrity repair exceeded the {CommandTimeout.TotalMinutes:0}-minute safety timeout.");
            }

            return new CommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record SystemImageRepairExecutionResult(
        bool Attempted,
        bool WindowsReportedSuccess,
        bool Verified,
        SystemImageRepairAction Action,
        string Summary,
        string ExecutionOutput,
        string ExecutionError)
    {
        public static SystemImageRepairExecutionResult NotRun(string summary) =>
            new(false, false, false, SystemImageRepairAction.None, summary, string.Empty, string.Empty);
    }
}
