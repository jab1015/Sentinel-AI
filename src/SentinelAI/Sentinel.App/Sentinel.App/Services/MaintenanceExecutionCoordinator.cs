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
    /// actually attempted a change, and returns one concise user-safe report.
    /// </summary>
    public sealed class MaintenanceExecutionCoordinator
    {
        private readonly NetworkRepairExecutor _networkRepairExecutor = new();
        private readonly PowerPlanOptimizationExecutor _powerPlanExecutor = new();
        private readonly WindowsUpdateRepairExecutor _windowsUpdateExecutor = new();
        private readonly SystemImageRepairExecutor _systemImageExecutor = new();
        private readonly BootStartupOptimizationExecutor _bootStartupExecutor = new();
        private readonly MaintenanceOutcomeRecorder _outcomeRecorder = new();
        private readonly MaintenanceReportService _reportService = new();

        public async Task<MaintenanceExecutionSummary> RunAsync(
            OptimizationSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();

            NetworkRepairExecutionResult network =
                await _networkRepairExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            _outcomeRecorder.Record(network);

            cancellationToken.ThrowIfCancellationRequested();

            PowerPlanOptimizationExecutionResult power =
                await _powerPlanExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            _outcomeRecorder.Record(power);

            cancellationToken.ThrowIfCancellationRequested();

            WindowsUpdateRepairExecutionResult windowsUpdate =
                await _windowsUpdateExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            _outcomeRecorder.Record(windowsUpdate);

            cancellationToken.ThrowIfCancellationRequested();

            SystemImageRepairExecutionResult systemImage =
                await _systemImageExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            _outcomeRecorder.Record(systemImage);

            cancellationToken.ThrowIfCancellationRequested();

            BootStartupOptimizationExecutionResult startup =
                _bootStartupExecutor.EvaluateAndExecute(settings);
            _outcomeRecorder.Record(startup);

            MaintenanceReport report = _reportService.BuildReport();

            int attempted = 0;
            if (network.Attempted) attempted++;
            if (power.Attempted) attempted++;
            if (windowsUpdate.Attempted) attempted++;
            if (systemImage.Attempted) attempted++;
            if (startup.Attempted) attempted++;

            return new MaintenanceExecutionSummary(
                attempted,
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
