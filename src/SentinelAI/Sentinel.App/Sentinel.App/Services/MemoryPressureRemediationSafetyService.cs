/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final safety gate for memory-pressure remediation. Sentinel may surface a
    /// user-action recommendation when evidence is persistent, but it never silently
    /// terminates or trims another application.
    /// </summary>
    public sealed class MemoryPressureRemediationSafetyService
    {
        public MemoryPressureRemediationSafetyAssessment Evaluate(
            MemoryPressureRemediationPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!plan.ActionWarranted)
                return MemoryPressureRemediationSafetyAssessment.Blocked(plan.Summary);

            MemoryPressureRemediationCandidate? recommendation = plan.Candidates
                .FirstOrDefault(candidate =>
                    candidate.Action == MemoryPressureRemediationAction.RequestUserApplicationClose &&
                    !candidate.Destructive);

            if (recommendation is null)
            {
                return MemoryPressureRemediationSafetyAssessment.Blocked(
                    "Sentinel found memory pressure but no safe user-action recommendation passed the final safety gate.");
            }

            if (recommendation.ProcessId <= 0 || string.IsNullOrWhiteSpace(recommendation.ProcessName))
            {
                return MemoryPressureRemediationSafetyAssessment.Blocked(
                    "Sentinel could not verify that the high-memory application is still running. No remediation action will be presented.");
            }

            return new MemoryPressureRemediationSafetyAssessment(
                true,
                false,
                recommendation,
                $"Persistent memory pressure is verified. Sentinel may recommend closing {recommendation.ProcessName} normally, but automatic force termination remains blocked.");
        }
    }

    public sealed record MemoryPressureRemediationSafetyAssessment(
        bool RecommendationAllowed,
        bool AutomaticExecutionAllowed,
        MemoryPressureRemediationCandidate? ApprovedRecommendation,
        string Summary)
    {
        public static MemoryPressureRemediationSafetyAssessment Blocked(string summary) =>
            new(false, false, null, summary);
    }
}
