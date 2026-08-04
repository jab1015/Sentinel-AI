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
    /// Executes only a power-plan change approved by the safety gate. The executor
    /// rechecks current evidence immediately before changing Windows and verifies
    /// the active plan afterward. A failed verification triggers rollback.
    /// </summary>
    public sealed class PowerPlanOptimizationExecutor
    {
        private readonly PowerPlanOptimizationPlanService _planService = new();
        private readonly PowerPlanOptimizationSafetyService _safetyService = new();
        private readonly PowerPlanHealthAssessmentService _healthService = new();

        public async Task<PowerPlanOptimizationExecutionResult> EvaluateAndExecuteAsync(
            OptimizationSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();

            PowerPlanOptimizationPlan plan = _planService.BuildPlan();
            PowerPlanOptimizationSafetyAssessment safety = _safetyService.Evaluate(plan, settings);

            if (!safety.ExecutionAllowed || safety.ApprovedCandidate is null)
            {
                return new(false, false, false, false, PowerPlanOptimizationAction.None,
                    safety.Summary, string.Empty, string.Empty);
            }

            PowerPlanOptimizationCandidate candidate = safety.ApprovedCandidate;
            if (candidate.Action != PowerPlanOptimizationAction.SwitchToBalanced)
            {
                return new(false, false, false, false, candidate.Action,
                    "Sentinel blocked an unsupported automatic power-plan change.", string.Empty, string.Empty);
            }

            PowerPlanOptimizationPlan freshPlan = _planService.BuildPlan();
            PowerPlanOptimizationCandidate? freshCandidate = freshPlan.Candidates
                .FirstOrDefault(c => c.Action == candidate.Action &&
                    c.TargetPlanGuid.Equals(candidate.TargetPlanGuid, StringComparison.OrdinalIgnoreCase));

            if (!freshPlan.ActionWarranted || freshCandidate is null || !freshPlan.PowerSource.OnAcPower)
            {
                return new(false, false, true, false, candidate.Action,
                    "The power condition changed before optimization. Sentinel made no system changes.", string.Empty, string.Empty);
            }

            PowerPlanHealthAssessment before = _healthService.Assess();
            if (!before.ActivePlanVerified || string.IsNullOrWhiteSpace(before.ActivePlanGuid))
            {
                return new(false, false, false, false, candidate.Action,
                    "Sentinel could not preserve the current power plan for rollback. No change was made.", string.Empty, string.Empty);
            }

            string originalGuid = before.ActivePlanGuid;
            CommandResult execution = await RunAsync("powercfg.exe",
                $"/setactive {candidate.TargetPlanGuid}", cancellationToken).ConfigureAwait(false);

            if (execution.ExitCode != 0)
            {
                return new(true, false, false, false, candidate.Action,
                    "Windows did not accept the power-plan optimization.", execution.Output, execution.Error);
            }

            PowerPlanHealthAssessment after = _healthService.Assess();
            bool verified = after.ActivePlanVerified &&
                after.ActivePlanGuid.Equals(candidate.TargetPlanGuid, StringComparison.OrdinalIgnoreCase);

            if (verified)
            {
                return new(true, true, true, false, candidate.Action,
                    "Sentinel switched Windows from Power saver to Balanced and verified the change.", execution.Output, execution.Error);
            }

            CommandResult rollback = await RunAsync("powercfg.exe",
                $"/setactive {originalGuid}", cancellationToken).ConfigureAwait(false);

            return new(true, true, false, rollback.ExitCode == 0, candidate.Action,
                rollback.ExitCode == 0
                    ? "Sentinel could not verify the optimized power plan, so it restored the previous plan."
                    : "Sentinel could not verify the optimized power plan and Windows did not confirm rollback. No further power changes will be attempted automatically.",
                execution.Output, string.Join(Environment.NewLine, execution.Error, rollback.Error));
        }

        private static async Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
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

            return new CommandResult(process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record PowerPlanOptimizationExecutionResult(
        bool Attempted,
        bool WindowsReportedSuccess,
        bool Verified,
        bool RolledBack,
        PowerPlanOptimizationAction Action,
        string Summary,
        string ExecutionOutput,
        string ExecutionError);
}
