/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final safety gate for automatic power-plan optimization. Sentinel permits
    /// only the reversible Power saver -> Balanced transition while AC power is
    /// positively verified. No High performance plan is selected automatically.
    /// </summary>
    public sealed class PowerPlanOptimizationSafetyService
    {
        public PowerPlanOptimizationSafetyAssessment Evaluate(
            PowerPlanOptimizationPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return PowerPlanOptimizationSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!plan.ActionWarranted || plan.Candidates.Count == 0)
                return PowerPlanOptimizationSafetyAssessment.Blocked(plan.Summary);

            if (!plan.Assessment.ActivePlanVerified ||
                plan.Assessment.Category != PowerPlanCategory.PowerSaver)
            {
                return PowerPlanOptimizationSafetyAssessment.Blocked(
                    "Sentinel blocked the power-plan change because Power saver is not currently verified as the active plan.");
            }

            if (!plan.PowerSource.Verified || !plan.PowerSource.OnAcPower)
            {
                return PowerPlanOptimizationSafetyAssessment.Blocked(
                    "Sentinel blocked the power-plan change because AC power is not currently verified.");
            }

            PowerPlanOptimizationCandidate? approved = plan.Candidates
                .FirstOrDefault(candidate =>
                    candidate.AutomaticEligible &&
                    candidate.Reversible &&
                    candidate.Action == PowerPlanOptimizationAction.SwitchToBalanced);

            if (approved is null)
            {
                return PowerPlanOptimizationSafetyAssessment.Blocked(
                    "No power-plan action passed Sentinel's automatic safety policy.");
            }

            return new PowerPlanOptimizationSafetyAssessment(
                true,
                settings.VerifyEveryChange,
                settings.RollBackWhenPossible,
                approved,
                plan.Assessment.ActivePlanGuid,
                "The reversible Power saver to Balanced change passed Sentinel's power-source and safety policy.");
        }
    }

    public sealed record PowerPlanOptimizationSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        bool RollbackRequired,
        PowerPlanOptimizationCandidate? ApprovedCandidate,
        string OriginalPlanGuid,
        string Summary)
    {
        public static PowerPlanOptimizationSafetyAssessment Blocked(string summary) =>
            new(false, true, true, null, string.Empty, summary);
    }
}
