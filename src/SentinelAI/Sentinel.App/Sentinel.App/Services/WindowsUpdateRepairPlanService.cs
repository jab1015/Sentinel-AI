/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts Windows Update health evidence into a conservative repair plan.
    /// This layer does not reset update components or change services.
    /// </summary>
    public sealed class WindowsUpdateRepairPlanService
    {
        private readonly WindowsUpdateHealthAssessmentService _assessmentService = new();

        public WindowsUpdateRepairPlan BuildPlan()
        {
            WindowsUpdateHealthAssessment assessment = _assessmentService.Assess();

            if (!assessment.RepairInvestigationWarranted)
            {
                return WindowsUpdateRepairPlan.NoAction(assessment, assessment.Summary);
            }

            var candidates = new List<WindowsUpdateRepairCandidate>();

            if (assessment.ServiceStopped)
            {
                candidates.Add(new WindowsUpdateRepairCandidate(
                    WindowsUpdateRepairAction.StartWindowsUpdateService,
                    "Start Windows Update service",
                    "Recent Windows Update failures were detected while the Windows Update service is stopped.",
                    AutomaticEligible: true,
                    RequiresElevation: true,
                    RequiresRestart: false));
            }

            candidates.Add(new WindowsUpdateRepairCandidate(
                WindowsUpdateRepairAction.ResetWindowsUpdateComponents,
                "Reset Windows Update components",
                "Recent Windows Update failures were detected. Component reset is a more disruptive repair and must not run automatically from a single evidence set.",
                AutomaticEligible: false,
                RequiresElevation: true,
                RequiresRestart: false));

            return new WindowsUpdateRepairPlan(
                assessment,
                true,
                candidates,
                assessment.ServiceStopped
                    ? "Sentinel identified a low-risk Windows Update service repair candidate. Component reset remains blocked pending stronger evidence."
                    : "Sentinel found recent Windows Update failures, but only the disruptive component-reset path remains and is not automatically eligible.");
        }
    }

    public sealed record WindowsUpdateRepairPlan(
        WindowsUpdateHealthAssessment Assessment,
        bool ActionWarranted,
        IReadOnlyList<WindowsUpdateRepairCandidate> Candidates,
        string Summary)
    {
        public static WindowsUpdateRepairPlan NoAction(
            WindowsUpdateHealthAssessment assessment,
            string summary) =>
            new(assessment, false, Array.Empty<WindowsUpdateRepairCandidate>(), summary);
    }

    public sealed record WindowsUpdateRepairCandidate(
        WindowsUpdateRepairAction Action,
        string Title,
        string Evidence,
        bool AutomaticEligible,
        bool RequiresElevation,
        bool RequiresRestart);

    public enum WindowsUpdateRepairAction
    {
        None,
        StartWindowsUpdateService,
        ResetWindowsUpdateComponents
    }
}
