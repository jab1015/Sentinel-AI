/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts maintenance executor results into one normalized history format.
    /// Executors remain focused on safety and verification while this service owns
    /// user-safe maintenance history persistence.
    /// </summary>
    public sealed class MaintenanceOutcomeRecorder
    {
        private readonly MaintenanceHistoryService _historyService = new();

        public void Record(NetworkRepairExecutionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            Record(
                "Network",
                result.Action.ToString(),
                result.Summary,
                result.Attempted,
                result.WindowsReportedSuccess,
                result.Verified,
                rolledBack: false,
                CombineTechnicalDetail(result.ExecutionOutput, result.ExecutionError));
        }

        public void Record(PowerPlanOptimizationExecutionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            Record(
                "Power",
                result.Action.ToString(),
                result.Summary,
                result.Attempted,
                result.WindowsReportedSuccess,
                result.Verified,
                result.RolledBack,
                CombineTechnicalDetail(result.ExecutionOutput, result.ExecutionError));
        }

        public void Record(WindowsUpdateRepairExecutionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            Record(
                "Windows Update",
                result.Action.ToString(),
                result.Summary,
                result.Attempted,
                result.WindowsReportedSuccess,
                result.Verified,
                rolledBack: false,
                CombineTechnicalDetail(result.ExecutionOutput, result.ExecutionError));
        }

        private void Record(
            string category,
            string action,
            string userSummary,
            bool attempted,
            bool successful,
            bool verified,
            bool rolledBack,
            string technicalDetail)
        {
            // No-op evaluations are intentionally not written as maintenance history;
            // normal healthy operation should remain quiet and uncluttered.
            if (!attempted && !rolledBack)
                return;

            _historyService.Record(new MaintenanceHistoryEntry(
                DateTimeOffset.UtcNow,
                category,
                action,
                userSummary,
                attempted,
                successful,
                verified,
                rolledBack,
                technicalDetail));
        }

        private static string CombineTechnicalDetail(string output, string error)
        {
            if (string.IsNullOrWhiteSpace(output))
                return error ?? string.Empty;

            if (string.IsNullOrWhiteSpace(error))
                return output;

            return string.Concat(output, Environment.NewLine, error);
        }
    }
}
