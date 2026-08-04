/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Completes the device-health pipeline by re-evaluating current device evidence
    /// immediately before presenting repair guidance. This service never modifies a
    /// device or driver.
    /// </summary>
    public sealed class DeviceHealthGuidanceService
    {
        private readonly DeviceHealthRemediationPlanService _planService = new();
        private readonly DeviceHealthRemediationSafetyService _safetyService = new();

        public DeviceHealthGuidanceResult Evaluate(OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            DeviceHealthRemediationPlan plan = _planService.BuildPlan();
            DeviceHealthRemediationSafetyAssessment safety = _safetyService.Evaluate(plan, settings);

            if (!safety.GuidanceAllowed || safety.ApprovedCandidate is null)
            {
                return new DeviceHealthGuidanceResult(
                    false,
                    DeviceHealthRemediationAction.None,
                    string.Empty,
                    string.Empty,
                    -1,
                    safety.Summary);
            }

            DeviceHealthRemediationCandidate candidate = safety.ApprovedCandidate;

            // Rebuild the plan immediately before returning guidance so stale device
            // evidence is never surfaced as a current recommendation.
            DeviceHealthRemediationPlan freshPlan = _planService.BuildPlan();
            DeviceHealthRemediationCandidate? freshCandidate = null;

            foreach (DeviceHealthRemediationCandidate item in freshPlan.Candidates)
            {
                if (item.Action == candidate.Action &&
                    item.InstanceId.Equals(candidate.InstanceId, StringComparison.OrdinalIgnoreCase) &&
                    item.ProblemCode == candidate.ProblemCode)
                {
                    freshCandidate = item;
                    break;
                }
            }

            if (!freshPlan.ActionWarranted || freshCandidate is null)
            {
                return new DeviceHealthGuidanceResult(
                    false,
                    DeviceHealthRemediationAction.None,
                    string.Empty,
                    string.Empty,
                    -1,
                    "The device condition changed before Sentinel presented guidance. No driver action is recommended now.");
            }

            return new DeviceHealthGuidanceResult(
                true,
                freshCandidate.Action,
                freshCandidate.InstanceId,
                freshCandidate.DeviceName,
                freshCandidate.ProblemCode,
                freshCandidate.Evidence);
        }
    }

    public sealed record DeviceHealthGuidanceResult(
        bool GuidanceAvailable,
        DeviceHealthRemediationAction Action,
        string InstanceId,
        string DeviceName,
        int ProblemCode,
        string Summary);
}
