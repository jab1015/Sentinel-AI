/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Produces a verified user-facing remediation recommendation for persistent
    /// memory pressure. This coordinator never terminates, suspends, trims, or
    /// otherwise changes another process.
    /// </summary>
    public sealed class MemoryPressureRemediationCoordinator
    {
        private readonly MemoryPressureRemediationPlanService _planService = new();
        private readonly MemoryPressureRemediationSafetyService _safetyService = new();

        public MemoryPressureRemediationResult Evaluate(OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            MemoryPressureRemediationPlan plan = _planService.BuildPlan();
            MemoryPressureRemediationSafetyAssessment safety = _safetyService.Evaluate(plan, settings);

            if (!safety.RecommendationAllowed || safety.ApprovedRecommendation is null)
            {
                return new MemoryPressureRemediationResult(
                    false,
                    false,
                    string.Empty,
                    0,
                    safety.Summary);
            }

            MemoryPressureRemediationCandidate recommendation = safety.ApprovedRecommendation;

            if (!IsProcessStillVerified(recommendation.ProcessId, recommendation.ProcessName))
            {
                return new MemoryPressureRemediationResult(
                    false,
                    false,
                    recommendation.ProcessName,
                    recommendation.ProcessId,
                    "The high-memory application is no longer running. Sentinel will continue monitoring and no action is required.");
            }

            string summary =
                $"{recommendation.ProcessName} has repeatedly used substantial memory while system memory pressure remained high. " +
                "Close the application normally if you are not actively using it. Sentinel will not force-close it or risk unsaved work.";

            return new MemoryPressureRemediationResult(
                true,
                false,
                recommendation.ProcessName,
                recommendation.ProcessId,
                summary);
        }

        private static bool IsProcessStillVerified(int processId, string processName)
        {
            if (processId <= 0 || string.IsNullOrWhiteSpace(processName))
                return false;

            try
            {
                using Process process = Process.GetProcessById(processId);
                return !process.HasExited &&
                    process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed record MemoryPressureRemediationResult(
        bool UserActionRecommended,
        bool AutomaticActionTaken,
        string ProcessName,
        int ProcessId,
        string Summary);
}
