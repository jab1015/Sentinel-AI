/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts measured Windows boot-performance evidence into a conservative
    /// optimization plan. This layer never disables startup software or changes
    /// Windows configuration.
    /// </summary>
    public sealed class BootPerformanceOptimizationPlanService
    {
        private readonly BootPerformanceAssessmentService _assessmentService = new();

        public BootPerformanceOptimizationPlan BuildPlan()
        {
            BootPerformanceAssessment assessment = _assessmentService.Assess();

            if (!assessment.OptimizationInvestigationWarranted)
            {
                return BootPerformanceOptimizationPlan.NoAction(
                    assessment,
                    assessment.Summary);
            }

            var candidates = new List<BootPerformanceOptimizationCandidate>();

            // Sustained slow boot is evidence to investigate startup impact, but it
            // is not sufficient evidence to silently disable third-party software.
            candidates.Add(new BootPerformanceOptimizationCandidate(
                BootPerformanceOptimizationAction.AnalyzeStartupImpact,
                "Analyze startup impact",
                "Windows boot history shows sustained startup slowdown. Sentinel should correlate this with measured startup-item impact before considering any change.",
                AutomaticEligible: true,
                ChangesSystemState: false));

            candidates.Add(new BootPerformanceOptimizationCandidate(
                BootPerformanceOptimizationAction.DisableHighImpactStartupItem,
                "Disable a verified high-impact startup item",
                "A startup item may be contributing to sustained boot slowdown, but disabling software changes user configuration and requires stronger item-specific evidence.",
                AutomaticEligible: false,
                ChangesSystemState: true));

            return new BootPerformanceOptimizationPlan(
                assessment,
                true,
                candidates,
                "Sentinel verified sustained startup slowdown. Startup-impact correlation is warranted; automatic disabling remains blocked until a specific nonessential item is proven responsible.");
        }
    }

    public sealed record BootPerformanceOptimizationPlan(
        BootPerformanceAssessment Assessment,
        bool ActionWarranted,
        IReadOnlyList<BootPerformanceOptimizationCandidate> Candidates,
        string Summary)
    {
        public static BootPerformanceOptimizationPlan NoAction(
            BootPerformanceAssessment assessment,
            string summary) =>
            new(assessment, false, Array.Empty<BootPerformanceOptimizationCandidate>(), summary);
    }

    public sealed record BootPerformanceOptimizationCandidate(
        BootPerformanceOptimizationAction Action,
        string Title,
        string Evidence,
        bool AutomaticEligible,
        bool ChangesSystemState);

    public enum BootPerformanceOptimizationAction
    {
        None,
        AnalyzeStartupImpact,
        DisableHighImpactStartupItem
    }
}
