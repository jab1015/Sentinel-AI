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
    /// Runs the supported verified maintenance executors, records only actions that
    /// actually attempted a change, and returns one concise user-safe report. Resource-intensive
    /// DISM/SFC diagnostics and repairs are excluded from unattended maintenance.
    /// </summary>
    public sealed class MaintenanceExecutionCoordinator
    {
        private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
        private readonly NetworkRepairExecutor _networkRepairExecutor = new();
        private readonly PowerPlanOptimizationExecutor _powerPlanExecutor = new();
        private readonly WindowsUpdateRepairExecutor _windowsUpdateExecutor = new();
        private readonly BootStartupOptimizationExecutor _bootStartupExecutor = new();
        private readonly MaintenanceOutcomeRecorder _outcomeRecorder = new();
        private readonly MaintenanceReportService _reportService = new();

        public async Task<MaintenanceExecutionSummary> RunAsync(
            OptimizationSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();
            await ExecutionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
            NetworkRepairExecutionResult network =
                await _networkRepairExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            _outcomeRecorder.Record(network);
            if (network.Attempted)
                return BuildSummary(actionsAttempted: 1);

            cancellationToken.ThrowIfCancellationRequested();

            PowerPlanOptimizationExecutionResult power =
                await _powerPlanExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            _outcomeRecorder.Record(power);
            if (power.Attempted)
                return BuildSummary(actionsAttempted: 1);

            cancellationToken.ThrowIfCancellationRequested();

            WindowsUpdateRepairExecutionResult windowsUpdate =
                await _windowsUpdateExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            _outcomeRecorder.Record(windowsUpdate);
            if (windowsUpdate.Attempted)
                return BuildSummary(actionsAttempted: 1);

            cancellationToken.ThrowIfCancellationRequested();

            BootStartupOptimizationExecutionResult startup =
                _bootStartupExecutor.EvaluateAndExecute(settings);
            _outcomeRecorder.Record(startup);
            return BuildSummary(startup.Attempted ? 1 : 0);
            }
            finally
            {
                ExecutionGate.Release();
            }
        }

        private MaintenanceExecutionSummary BuildSummary(int actionsAttempted)
        {
            MaintenanceReport report = _reportService.BuildReport();
            return new MaintenanceExecutionSummary(
                actionsAttempted,
                report.UserActionRequired,
                report.Headline,
                report.Summary,
                report);
        }
    }

    public sealed record MaintenanceExecutionSummary(
        int ActionsAttempted,
        bool UserActionRequired,
        string Headline,
        string Summary,
        MaintenanceReport Report);
}
