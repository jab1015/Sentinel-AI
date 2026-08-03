/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Applies the user's optimization preferences as a final safety gate before
    /// any optimization executor is allowed to change Windows.
    /// </summary>
    public sealed class OptimizationSafetyService
    {
        public OptimizationSafetyAssessment Evaluate(
            OptimizationDecision decision,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(decision);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return OptimizationSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!decision.OptimizationWarranted)
            {
                return OptimizationSafetyAssessment.Blocked(decision.Summary);
            }

            if (!decision.AutomaticChangeAllowed)
            {
                return OptimizationSafetyAssessment.Blocked(
                    "Sentinel has not verified an optimization that is safe to apply automatically.");
            }

            foreach (OptimizationCandidate candidate in decision.Candidates)
            {
                if (!candidate.AutomaticEligible || candidate.Risk != OptimizationRisk.Low)
                {
                    return OptimizationSafetyAssessment.Blocked(
                        "The proposed optimization requires investigation or user review before a change can be made.");
                }
            }

            if (decision.Candidates.Count == 0)
            {
                return OptimizationSafetyAssessment.Blocked(
                    "No optimization action is available.");
            }

            // Conservative mode is intentionally strict. Balanced and Advanced can
            // gain broader policies later, but never bypass verified evidence.
            if (settings.Mode == OptimizationMode.Conservative &&
                decision.Candidates.Count > 1)
            {
                return OptimizationSafetyAssessment.Blocked(
                    "Conservative mode will not apply multiple automatic changes at once.");
            }

            return new OptimizationSafetyAssessment(
                true,
                settings.VerifyEveryChange,
                settings.RollBackWhenPossible,
                "The proposed optimization passed Sentinel's automatic-change safety policy.");
        }
    }

    public sealed record OptimizationSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        bool RollbackRequiredWhenPossible,
        string Summary)
    {
        public static OptimizationSafetyAssessment Blocked(string summary) =>
            new(false, true, true, summary);
    }
}
