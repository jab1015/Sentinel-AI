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
    /// Executes only network repairs approved by NetworkRepairSafetyService.
    /// Current automatic scope is intentionally limited to DNS resolver-cache flush.
    /// Disruptive actions such as Winsock reset are never executed here automatically.
    /// </summary>
    public sealed class NetworkRepairExecutor
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
        private readonly NetworkRepairPlanService _planService = new();
        private readonly NetworkRepairSafetyService _safetyService = new();
        private readonly NetworkHealthAssessmentService _healthService = new();

        public async Task<NetworkRepairExecutionResult> EvaluateAndExecuteAsync(
            OptimizationSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            NetworkRepairPlan plan =
                await _planService.BuildPlanAsync(cancellationToken).ConfigureAwait(false);

            NetworkRepairSafetyAssessment safety = _safetyService.Evaluate(plan, settings);

            if (!safety.ExecutionAllowed || safety.ApprovedCandidate is null)
            {
                return new NetworkRepairExecutionResult(
                    false,
                    false,
                    false,
                    NetworkRepairAction.None,
                    safety.Summary,
                    string.Empty,
                    string.Empty);
            }

            NetworkRepairCandidate candidate = safety.ApprovedCandidate;

            if (candidate.Action != NetworkRepairAction.FlushDnsCache)
            {
                return new NetworkRepairExecutionResult(
                    false,
                    false,
                    false,
                    candidate.Action,
                    "Sentinel blocked this network repair because it is outside the approved low-risk automatic repair scope.",
                    string.Empty,
                    string.Empty);
            }

            // Re-check immediately before changing anything. A transient DNS failure
            // that has already recovered must not trigger an unnecessary repair.
            NetworkHealthAssessment preExecution =
                await _healthService.AssessAsync(cancellationToken).ConfigureAwait(false);

            if (!preExecution.RepairInvestigationWarranted || preExecution.DnsResolutionSucceeded)
            {
                return new NetworkRepairExecutionResult(
                    false,
                    false,
                    true,
                    candidate.Action,
                    "The DNS condition recovered before repair. Sentinel made no network changes.",
                    string.Empty,
                    string.Empty);
            }

            CommandResult execution = await RunAsync(
                "ipconfig.exe",
                "/flushdns",
                cancellationToken).ConfigureAwait(false);

            if (execution.ExitCode != 0)
            {
                return new NetworkRepairExecutionResult(
                    true,
                    false,
                    false,
                    candidate.Action,
                    "Windows did not complete the DNS cache repair successfully. Sentinel made no further network changes.",
                    execution.Output,
                    execution.Error);
            }

            if (!safety.VerificationRequired)
            {
                return new NetworkRepairExecutionResult(
                    true,
                    true,
                    false,
                    candidate.Action,
                    "Windows completed the DNS cache repair.",
                    execution.Output,
                    execution.Error);
            }

            NetworkHealthAssessment verification =
                await _healthService.AssessAsync(cancellationToken).ConfigureAwait(false);

            bool verified = verification.DnsResolutionSucceeded;
            string summary = verified
                ? "Sentinel repaired the DNS resolver cache and verified that name resolution is working."
                : "Windows completed the DNS cache repair, but Sentinel could not verify restored name resolution. No more disruptive automatic network repair will be attempted.";

            return new NetworkRepairExecutionResult(
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
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            ProcessExecutionResult result = await BoundedProcessRunner.RunAsync(
                startInfo,
                CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            if (result.Outcome == ProcessExecutionOutcome.Canceled && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            int exitCode = result.ExitCode ?? -1;
            string error = result.StandardError;
            if (result.Outcome == ProcessExecutionOutcome.TimedOut)
                error = $"Windows network repair command exceeded the {CommandTimeout.TotalSeconds:0}-second safety timeout. {result.Detail}".Trim();
            else if (result.Outcome is ProcessExecutionOutcome.LaunchFailure or ProcessExecutionOutcome.OutputReadFailure)
                error = string.IsNullOrWhiteSpace(error) ? result.Detail : error + Environment.NewLine + result.Detail;

            return new CommandResult(exitCode, result.StandardOutput, error);
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record NetworkRepairExecutionResult(
        bool Attempted,
        bool WindowsReportedSuccess,
        bool Verified,
        NetworkRepairAction Action,
        string Summary,
        string ExecutionOutput,
        string ExecutionError);
}
