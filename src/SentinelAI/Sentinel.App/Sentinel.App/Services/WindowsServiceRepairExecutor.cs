/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Executes only a service restart/start that has passed Sentinel's repair
    /// planning and safety policy. The condition is revalidated immediately before
    /// execution and verified afterward. Service configuration is never changed.
    /// </summary>
    public sealed class WindowsServiceRepairExecutor
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
        private readonly WindowsServiceHealthAssessmentService _assessmentService = new();

        public async Task<WindowsServiceRepairExecutionResult> ExecuteAsync(
            WindowsServiceRepairSafetyAssessment safety,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(safety);

            if (!safety.ExecutionAllowed || safety.ApprovedCandidate is null)
            {
                return WindowsServiceRepairExecutionResult.NotRun(safety.Summary);
            }

            WindowsServiceRepairCandidate candidate = safety.ApprovedCandidate;
            if (candidate.Action != WindowsServiceRepairAction.Restart)
            {
                return WindowsServiceRepairExecutionResult.NotRun(
                    "The approved service action is not eligible for automatic execution.");
            }

            WindowsServiceHealthAssessment before = _assessmentService.Assess();
            WindowsServiceEvidence? current = before.Services.FirstOrDefault(service =>
                service.ServiceName.Equals(candidate.ServiceName, StringComparison.OrdinalIgnoreCase));

            if (current is null || !current.Exists)
            {
                return WindowsServiceRepairExecutionResult.NotRun(
                    $"{candidate.DisplayName} could not be revalidated, so Sentinel made no change.");
            }

            if (current.Concern == ServiceHealthConcern.Disabled)
            {
                return WindowsServiceRepairExecutionResult.NotRun(
                    $"{candidate.DisplayName} is disabled. Sentinel will not change its configured startup state automatically.");
            }

            if (!current.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
            {
                return new WindowsServiceRepairExecutionResult(
                    false,
                    true,
                    true,
                    candidate.ServiceName,
                    candidate.DisplayName,
                    "The service recovered before repair was needed. Sentinel made no change.",
                    string.Empty,
                    string.Empty);
            }

            CommandResult command = await RunAsync(
                "sc.exe",
                $"start \"{candidate.ServiceName}\"",
                cancellationToken).ConfigureAwait(false);

            if (command.ExitCode != 0)
            {
                bool accessDenied =
                    command.Output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    command.Error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    command.Output.Contains("FAILED 5", StringComparison.OrdinalIgnoreCase) ||
                    command.Error.Contains("FAILED 5", StringComparison.OrdinalIgnoreCase);

                return new WindowsServiceRepairExecutionResult(
                    true,
                    false,
                    false,
                    candidate.ServiceName,
                    candidate.DisplayName,
                    accessDenied
                        ? "Sentinel verified the service repair but Windows requires elevated permission to perform it. No configuration was changed."
                        : "Windows did not complete the verified service repair. Sentinel made no further changes.",
                    command.Output,
                    command.Error);
            }

            bool verifiedRunning = false;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                    .ConfigureAwait(false);

                WindowsServiceHealthAssessment after = _assessmentService.Assess();
                WindowsServiceEvidence? verified = after.Services.FirstOrDefault(service =>
                    service.ServiceName.Equals(candidate.ServiceName, StringComparison.OrdinalIgnoreCase));

                if (verified is not null &&
                    verified.State.Equals("Running", StringComparison.OrdinalIgnoreCase) &&
                    verified.Concern == ServiceHealthConcern.None)
                {
                    verifiedRunning = true;
                    break;
                }
            }

            return new WindowsServiceRepairExecutionResult(
                true,
                true,
                verifiedRunning,
                candidate.ServiceName,
                candidate.DisplayName,
                verifiedRunning
                    ? $"Sentinel restored {candidate.DisplayName} and verified that it is running normally."
                    : $"Windows accepted the repair for {candidate.DisplayName}, but Sentinel could not verify a healthy running state. No additional automatic change will be made now.",
                command.Output,
                command.Error);
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
                CreateNoWindow = true
            };

            ProcessExecutionResult execution = await BoundedProcessRunner.RunAsync(
                startInfo,
                CommandTimeout,
                cancellationToken,
                maxOutputChars: 128_000).ConfigureAwait(false);

            if (execution.Outcome == ProcessExecutionOutcome.Canceled && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            string error = execution.Outcome == ProcessExecutionOutcome.TimedOut
                ? $"Windows service repair command exceeded the {CommandTimeout.TotalSeconds:0}-second safety timeout."
                : execution.StandardError.Length > 0 ? execution.StandardError : execution.Detail;

            return new CommandResult(
                execution.Succeeded ? 0 : execution.ExitCode ?? -1,
                execution.StandardOutput,
                error);
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record WindowsServiceRepairExecutionResult(
        bool Attempted,
        bool WindowsReportedSuccess,
        bool Verified,
        string ServiceName,
        string DisplayName,
        string Summary,
        string CommandOutput,
        string CommandError)
    {
        public static WindowsServiceRepairExecutionResult NotRun(string summary) =>
            new(false, false, false, string.Empty, string.Empty, summary, string.Empty, string.Empty);
    }
}
