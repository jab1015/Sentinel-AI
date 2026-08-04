/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Executes only Windows Update repairs approved by the safety gate.
    /// Automatic scope is intentionally limited to starting the Windows Update
    /// service when recent update failures and a stopped service are both verified.
    /// </summary>
    public sealed class WindowsUpdateRepairExecutor
    {
        private readonly WindowsUpdateRepairPlanService _planService = new();
        private readonly WindowsUpdateRepairSafetyService _safetyService = new();
        private readonly WindowsUpdateHealthAssessmentService _healthService = new();

        public async Task<WindowsUpdateRepairExecutionResult> EvaluateAndExecuteAsync(
            OptimizationSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();

            WindowsUpdateRepairPlan plan = _planService.BuildPlan();
            WindowsUpdateRepairSafetyAssessment safety = _safetyService.Evaluate(plan, settings);

            if (!safety.ExecutionAllowed || safety.ApprovedCandidate is null)
            {
                return new WindowsUpdateRepairExecutionResult(
                    false, false, false, WindowsUpdateRepairAction.None,
                    safety.Summary, string.Empty, string.Empty);
            }

            WindowsUpdateRepairCandidate candidate = safety.ApprovedCandidate;
            if (candidate.Action != WindowsUpdateRepairAction.StartWindowsUpdateService)
            {
                return new WindowsUpdateRepairExecutionResult(
                    false, false, false, candidate.Action,
                    "Sentinel blocked this Windows Update repair because it is outside the approved automatic repair scope.",
                    string.Empty, string.Empty);
            }

            // Re-check immediately before execution so a transient condition that
            // already recovered never causes an unnecessary service change.
            WindowsUpdateHealthAssessment preExecution = _healthService.Assess();
            if (!preExecution.ServiceExists || !preExecution.ServiceStopped ||
                !preExecution.RecentFailuresDetected)
            {
                return new WindowsUpdateRepairExecutionResult(
                    false, false, true, candidate.Action,
                    "The Windows Update condition changed before repair. Sentinel made no system changes.",
                    string.Empty, string.Empty);
            }

            CommandResult execution = await RunAsync(
                "sc.exe", "start wuauserv", cancellationToken).ConfigureAwait(false);

            // sc.exe may report that the service is already running if Windows
            // recovered between the final assessment and command execution.
            bool commandAccepted = execution.ExitCode == 0 ||
                execution.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
                execution.Output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase) ||
                execution.Output.Contains("already running", StringComparison.OrdinalIgnoreCase);

            if (!commandAccepted)
            {
                return new WindowsUpdateRepairExecutionResult(
                    true, false, false, candidate.Action,
                    "Windows did not complete the low-risk Windows Update service repair. Sentinel made no further update changes.",
                    execution.Output, execution.Error);
            }

            if (!safety.VerificationRequired)
            {
                return new WindowsUpdateRepairExecutionResult(
                    true, true, false, candidate.Action,
                    "Windows accepted the Windows Update service repair.",
                    execution.Output, execution.Error);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            WindowsUpdateHealthAssessment verification = _healthService.Assess();
            bool verified = verification.ServiceExists && verification.ServiceRunning;

            return new WindowsUpdateRepairExecutionResult(
                true,
                true,
                verified,
                candidate.Action,
                verified
                    ? "Sentinel started the Windows Update service and verified that it is running."
                    : "Windows accepted the service-start request, but Sentinel could not verify that Windows Update is running. No component reset will be attempted automatically.",
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
                    RedirectStandardError = true
                }
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new CommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record WindowsUpdateRepairExecutionResult(
        bool Attempted,
        bool WindowsReportedSuccess,
        bool Verified,
        WindowsUpdateRepairAction Action,
        string Summary,
        string ExecutionOutput,
        string ExecutionError);
}
