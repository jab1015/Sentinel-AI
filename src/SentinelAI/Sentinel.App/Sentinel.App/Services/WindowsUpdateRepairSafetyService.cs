/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final safety gate for Windows Update repair. Only the low-risk service-start
    /// action may be approved automatically. Component resets remain blocked.
    /// </summary>
    public sealed class WindowsUpdateRepairSafetyService
    {
        public WindowsUpdateRepairSafetyAssessment Evaluate(
            WindowsUpdateRepairPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return WindowsUpdateRepairSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!plan.ActionWarranted || plan.Candidates.Count == 0)
            {
                return WindowsUpdateRepairSafetyAssessment.Blocked(plan.Summary);
            }

            WindowsUpdateRepairCandidate? approved = plan.Candidates
                .FirstOrDefault(candidate => candidate.AutomaticEligible);

            if (approved is null)
            {
                return WindowsUpdateRepairSafetyAssessment.Blocked(
                    "Sentinel found Windows Update repair evidence, but no action passed the automatic safety policy.");
            }

            if (approved.Action != WindowsUpdateRepairAction.StartWindowsUpdateService)
            {
                return WindowsUpdateRepairSafetyAssessment.Blocked(
                    "Automatic Windows Update repair is limited to the low-risk service-start action. Component resets require explicit user approval.");
            }

            if (!plan.Assessment.ServiceExists || !plan.Assessment.ServiceStopped)
            {
                return WindowsUpdateRepairSafetyAssessment.Blocked(
                    "Sentinel blocked the update repair because the Windows Update service is not currently verified as stopped.");
            }

            if (!plan.Assessment.RecentFailuresDetected)
            {
                return WindowsUpdateRepairSafetyAssessment.Blocked(
                    "Sentinel blocked the update repair because recent Windows Update failure evidence was not verified.");
            }

            return new WindowsUpdateRepairSafetyAssessment(
                true,
                settings.VerifyEveryChange,
                approved,
                "The low-risk Windows Update service repair passed Sentinel's evidence and safety policy.");
        }
    }

    public sealed record WindowsUpdateRepairSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        WindowsUpdateRepairCandidate? ApprovedCandidate,
        string Summary)
    {
        public static WindowsUpdateRepairSafetyAssessment Blocked(string summary) =>
            new(false, true, null, summary);
    }
}
