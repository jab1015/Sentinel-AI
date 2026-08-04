/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final safety gate for device-health remediation. Device and driver changes
    /// can interrupt hardware or destabilize Windows, so Sentinel permits guidance
    /// only and blocks unattended driver modification.
    /// </summary>
    public sealed class DeviceHealthRemediationSafetyService
    {
        public DeviceHealthRemediationSafetyAssessment Evaluate(
            DeviceHealthRemediationPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return DeviceHealthRemediationSafetyAssessment.Blocked(
                    "Automatic optimization is disabled. Sentinel will not perform device-health changes.");
            }

            if (!plan.Assessment.DeviceHealthVerified || !plan.ActionWarranted)
            {
                return DeviceHealthRemediationSafetyAssessment.Blocked(plan.Summary);
            }

            DeviceHealthRemediationCandidate? guidance = plan.Candidates
                .FirstOrDefault(candidate =>
                    candidate.Action == DeviceHealthRemediationAction.RecommendWindowsUpdateDriverCheck &&
                    !candidate.Destructive);

            if (guidance is null)
            {
                return DeviceHealthRemediationSafetyAssessment.Blocked(
                    "Sentinel verified a device problem but found no safe remediation guidance.");
            }

            return new DeviceHealthRemediationSafetyAssessment(
                false,
                true,
                guidance,
                "Sentinel may present verified device-repair guidance. Automatic driver disablement, removal, rollback, reinstall, and replacement remain blocked.");
        }
    }

    public sealed record DeviceHealthRemediationSafetyAssessment(
        bool ExecutionAllowed,
        bool GuidanceAllowed,
        DeviceHealthRemediationCandidate? ApprovedCandidate,
        string Summary)
    {
        public static DeviceHealthRemediationSafetyAssessment Blocked(string summary) =>
            new(false, false, null, summary);
    }
}
