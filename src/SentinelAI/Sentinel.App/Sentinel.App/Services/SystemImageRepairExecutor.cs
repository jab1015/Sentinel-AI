/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
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

            SystemImageRepairPlan plan = await _planService.BuildPlanAsync(cancellationToken).ConfigureAwait(false);
            SystemImageRepairSafetyAssessment safety = _safetyService.Evaluate(plan, settings);
            if (!safety.ExecutionAllowed || safety.ApprovedCandidate is null)
                return SystemImageRepairExecutionResult.NotRun(safety.Summary);

            SystemImageRepairCandidate candidate = safety.ApprovedCandidate;
            SystemImageHealthAssessment before = await _assessmentService.AssessAsync(cancellationToken).ConfigureAwait(false);

            bool stillWarranted = candidate.Action switch
            {
                SystemImageRepairAction.RestoreComponentStore => before.ComponentStoreState == IntegrityAssessmentState.Corrupt,
                SystemImageRepairAction.RepairProtectedFiles =>
                    before.ProtectedFilesState == IntegrityAssessmentState.Corrupt &&
                    before.ComponentStoreState == IntegrityAssessmentState.Healthy,
                _ => false
            };

            if (!stillWarranted)
            {
                return new SystemImageRepairExecutionResult(
                    false, true, true, candidate.Action,
                    "The Windows integrity evidence no longer positively matches the approved corruption condition. Sentinel made no change.",
                    string.Empty, string.Empty);
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
                return SystemImageRepairExecutionResult.NotRun("The approved Windows integrity action is not supported by the repair executor.");
            }

            CommandResult execution = await RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
            if (execution.Outcome != ProcessExecutionOutcome.Succeeded || execution.ExitCode != 0)
            {
                return new SystemImageRepairExecutionResult(
                    true, false, false, candidate.Action,
                    execution.Outcome switch
                    {
                        ProcessExecutionOutcome.TimedOut => "The Windows integrity repair exceeded its safety timeout and was terminated. Sentinel did not report success.",
                        ProcessExecutionOutcome.Canceled => "The Windows integrity repair was canceled and Sentinel did not report success.",
                        _ => "Windows did not complete the integrity repair successfully. Sentinel did not report success."
                    },
                    execution.Output, execution.Error);
            }

            if (!safety.VerificationRequired)
            {
                return new SystemImageRepairExecutionResult(
                    true, true, false, candidate.Action,
                    "Windows completed the integrity command, but Sentinel's policy did not request a post-check. The action is not being presented as a verified repair.",
                    execution.Output, execution.Error);
            }

            SystemImageHealthAssessment after = await _assessmentService.AssessAsync(cancellationToken).ConfigureAwait(false);
            bool verified = candidate.Action switch
            {
                SystemImageRepairAction.RestoreComponentStore => after.ComponentStoreState == IntegrityAssessmentState.Healthy,
                SystemImageRepairAction.RepairProtectedFiles => after.ProtectedFilesState == IntegrityAssessmentState.Healthy,
                _ => false
            };

            string summary = verified
                ? candidate.Action == SystemImageRepairAction.RestoreComponentStore
                    ? "Sentinel ran the Windows component-store repair and a fresh read-only assessment explicitly reported the component store healthy."
                    : "Sentinel ran protected-file repair and a fresh read-only SFC verification explicitly reported no integrity violations."
                : "Windows returned success from the repair command, but the fresh health assessment did not positively report a healthy state. Sentinel did not mark the repair verified.";

            return new SystemImageRepairExecutionResult(
                true, true, verified, candidate.Action, summary, execution.Output, execution.Error);
        }

        private static async Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string absolutePath = string.IsNullOrWhiteSpace(system) ? fileName : Path.Combine(system, fileName);
            ProcessStartInfo startInfo = new()
            {
                FileName = absolutePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            ProcessExecutionResult result = await BoundedProcessRunner.RunAsync(
                startInfo, CommandTimeout, cancellationToken).ConfigureAwait(false);
            return new CommandResult(result.ExitCode ?? -1, result.StandardOutput, result.StandardError, result.Outcome);
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error, ProcessExecutionOutcome Outcome);
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
