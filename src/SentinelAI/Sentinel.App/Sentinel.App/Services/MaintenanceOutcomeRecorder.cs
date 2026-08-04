/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts maintenance, containment, and remediation results into one normalized
    /// history format. Executors remain focused on safety and verification while this
    /// service owns user-safe history persistence.
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

        public void Record(SystemImageRepairExecutionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            Record(
                "Windows Integrity",
                result.Action.ToString(),
                result.Summary,
                result.Attempted,
                result.WindowsReportedSuccess,
                result.Verified,
                rolledBack: false,
                CombineTechnicalDetail(result.ExecutionOutput, result.ExecutionError));
        }

        public void Record(BootStartupOptimizationExecutionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            string action = string.IsNullOrWhiteSpace(result.StartupItemName)
                ? "Startup optimization"
                : $"Startup: {result.StartupItemName}";

            Record(
                "Startup",
                action,
                result.Summary,
                result.Attempted,
                result.Changed,
                result.Verified,
                result.RolledBack,
                technicalDetail: string.Empty);
        }

        public void Record(ProcessContainmentService.ProcessContainmentResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            string action = string.IsNullOrWhiteSpace(result.ProcessName)
                ? "Contain suspicious process"
                : result.ProcessId.HasValue
                    ? $"Contain {result.ProcessName} (PID {result.ProcessId.Value})"
                    : $"Contain {result.ProcessName}";

            Record(
                "Protection",
                action,
                result.Summary,
                result.Attempted,
                result.Succeeded,
                result.Succeeded,
                rolledBack: false,
                technicalDetail: result.Title);
        }

        public void Record(FirewallContainmentService.FirewallContainmentResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            string action = string.IsNullOrWhiteSpace(result.RemoteIp)
                ? "Block suspicious network destination"
                : $"Block outbound endpoint {result.RemoteIp}";

            Record(
                "Protection",
                action,
                result.Summary,
                result.Attempted,
                result.Succeeded,
                result.Succeeded || result.RolledBack,
                result.RolledBack,
                technicalDetail: string.IsNullOrWhiteSpace(result.RuleName)
                    ? result.Title
                    : $"{result.Title}{Environment.NewLine}Rule: {result.RuleName}");
        }

        public void Record(QuarantineService.QuarantineResult result, string action)
        {
            ArgumentNullException.ThrowIfNull(result);

            bool attempted = !result.RequiresUserApproval;
            if (!attempted && !result.Succeeded)
                return;

            Record(
                "Protection",
                string.IsNullOrWhiteSpace(action) ? "Quarantine operation" : action,
                result.Message,
                attempted,
                result.Succeeded,
                result.Verified,
                rolledBack: action.Contains("restore", StringComparison.OrdinalIgnoreCase) && result.Succeeded,
                technicalDetail: result.Sha256 ?? string.Empty);
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
