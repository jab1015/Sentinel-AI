/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts verified service-health evidence into a conservative repair plan.
    /// This layer is read-only and never changes service configuration or state.
    /// </summary>
    public sealed class WindowsServiceRepairPlanService
    {
        private readonly WindowsServiceHealthAssessmentService _assessmentService = new();

        public WindowsServiceRepairPlan BuildPlan()
        {
            WindowsServiceHealthAssessment assessment = _assessmentService.Assess();

            if (!assessment.RepairInvestigationWarranted)
            {
                return WindowsServiceRepairPlan.NoAction(assessment, assessment.Summary);
            }

            var candidates = new List<WindowsServiceRepairCandidate>();

            foreach (WindowsServiceEvidence service in assessment.Concerns)
            {
                if (!service.Exists || service.Concern == ServiceHealthConcern.Unverified)
                    continue;

                // A disabled service may reflect an administrator or organizational
                // policy. Sentinel must not silently change its startup configuration.
                if (service.Concern == ServiceHealthConcern.Disabled)
                {
                    candidates.Add(new WindowsServiceRepairCandidate(
                        service.ServiceName,
                        service.DisplayName,
                        WindowsServiceRepairAction.ReviewConfiguration,
                        false,
                        $"{service.DisplayName} is disabled. Sentinel will not change a disabled service without stronger evidence that the configuration is unintended."));
                    continue;
                }

                if (service.Concern == ServiceHealthConcern.UnexpectedlyStopped)
                {
                    bool restartEligible =
                        service.Importance == ServiceImportance.Core ||
                        service.Importance == ServiceImportance.Security;

                    candidates.Add(new WindowsServiceRepairCandidate(
                        service.ServiceName,
                        service.DisplayName,
                        WindowsServiceRepairAction.Restart,
                        restartEligible,
                        $"{service.DisplayName} is expected to support core or security functionality but is currently stopped. A restart may be appropriate after Sentinel confirms the state persists."));
                }
            }

            if (candidates.Count == 0)
            {
                return WindowsServiceRepairPlan.NoAction(
                    assessment,
                    "Sentinel found service-health evidence but no repair action is safe to plan automatically.");
            }

            bool hasAutomaticCandidate = candidates.Any(candidate => candidate.AutomaticEligible);

            return new WindowsServiceRepairPlan(
                assessment,
                true,
                hasAutomaticCandidate,
                candidates,
                hasAutomaticCandidate
                    ? "Sentinel identified a potentially repairable Windows service condition. Persistence and safety must be verified before execution."
                    : "Sentinel identified a service configuration that requires review rather than an automatic change.");
        }
    }

    public sealed record WindowsServiceRepairPlan(
        WindowsServiceHealthAssessment Assessment,
        bool ActionWarranted,
        bool HasAutomaticCandidate,
        IReadOnlyList<WindowsServiceRepairCandidate> Candidates,
        string Summary)
    {
        public static WindowsServiceRepairPlan NoAction(
            WindowsServiceHealthAssessment assessment,
            string summary) =>
            new(assessment, false, false, Array.Empty<WindowsServiceRepairCandidate>(), summary);
    }

    public sealed record WindowsServiceRepairCandidate(
        string ServiceName,
        string DisplayName,
        WindowsServiceRepairAction Action,
        bool AutomaticEligible,
        string Evidence);

    public enum WindowsServiceRepairAction
    {
        None,
        Restart,
        ReviewConfiguration
    }
}
