/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final policy gate before any automatic Windows-service repair can occur.
    /// Disabled services are never silently reconfigured. Automatic repair is
    /// restricted to one verified restart candidate in Conservative mode.
    /// </summary>
    public sealed class WindowsServiceRepairSafetyService
    {
        public WindowsServiceRepairSafetyAssessment Evaluate(
            WindowsServiceRepairPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return WindowsServiceRepairSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!plan.ActionWarranted || plan.Candidates.Count == 0)
            {
                return WindowsServiceRepairSafetyAssessment.Blocked(plan.Summary);
            }

            WindowsServiceRepairCandidate[] eligible = plan.Candidates
                .Where(candidate =>
                    candidate.AutomaticEligible &&
                    candidate.Action == WindowsServiceRepairAction.Restart)
                .ToArray();

            if (eligible.Length == 0)
            {
                return WindowsServiceRepairSafetyAssessment.Blocked(
                    "Sentinel found a service condition, but no automatic repair passed the safety policy.");
            }

            WindowsServiceRepairCandidate approved = eligible[0];

            return new WindowsServiceRepairSafetyAssessment(
                true,
                settings.VerifyEveryChange,
                settings.RollBackWhenPossible,
                approved,
                settings.Mode == OptimizationMode.Conservative
                    ? "One verified service restart is approved for this repair cycle."
                    : "A verified service restart passed Sentinel's repair safety policy.");
        }
    }

    public sealed record WindowsServiceRepairSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        bool RollbackRequestedWhenPossible,
        WindowsServiceRepairCandidate? ApprovedCandidate,
        string Summary)
    {
        public static WindowsServiceRepairSafetyAssessment Blocked(string summary) =>
            new(false, true, true, null, summary);
    }
}
