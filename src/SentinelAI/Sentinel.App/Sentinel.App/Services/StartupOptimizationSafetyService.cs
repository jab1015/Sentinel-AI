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
    /// Final safety gate for startup optimization. Sentinel may identify costly
    /// startup items, but it must not silently disable user applications without
    /// verified boot-impact and user-need evidence.
    /// </summary>
    public sealed class StartupOptimizationSafetyService
    {
        public StartupOptimizationSafetyAssessment Evaluate(
            StartupOptimizationPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return StartupOptimizationSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!plan.ActionWarranted || plan.Candidates.Count == 0)
            {
                return StartupOptimizationSafetyAssessment.Blocked(plan.Summary);
            }

            StartupOptimizationCandidate[] automaticallyEligible = plan.Candidates
                .Where(candidate => candidate.AutomaticDisableAllowed)
                .ToArray();

            if (automaticallyEligible.Length == 0)
            {
                return new StartupOptimizationSafetyAssessment(
                    false,
                    true,
                    plan.Candidates,
                    "Sentinel found startup items with measurable cost, but none has enough evidence for silent automatic disabling. No startup setting will be changed.");
            }

            // Even when future evidence makes an item automatically eligible,
            // Conservative mode permits only one startup change per verified cycle.
            IReadOnlyList<StartupOptimizationCandidate> approved = settings.Mode == OptimizationMode.Conservative
                ? automaticallyEligible.Take(1).ToArray()
                : automaticallyEligible;

            return new StartupOptimizationSafetyAssessment(
                true,
                settings.VerifyEveryChange,
                approved,
                "The approved startup optimization candidates passed Sentinel's safety policy.");
        }
    }

    public sealed record StartupOptimizationSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        IReadOnlyList<StartupOptimizationCandidate> ApprovedCandidates,
        string Summary)
    {
        public static StartupOptimizationSafetyAssessment Blocked(string summary) =>
            new(false, true, Array.Empty<StartupOptimizationCandidate>(), summary);
    }
}
