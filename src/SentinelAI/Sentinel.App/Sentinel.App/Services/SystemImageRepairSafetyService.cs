/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final safety gate before DISM/SFC repair execution. Only a repair action
    /// supported by verified corruption evidence can be recommended, but execution requires explicit user approval.
    /// </summary>
    public sealed class SystemImageRepairSafetyService
    {
        public SystemImageRepairSafetyAssessment Evaluate(
            SystemImageRepairPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return SystemImageRepairSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!plan.ActionWarranted || plan.Candidates.Count == 0)
            {
                return SystemImageRepairSafetyAssessment.Blocked(plan.Summary);
            }

            SystemImageRepairCandidate? approved = plan.Candidates
                .FirstOrDefault(candidate => candidate.AutomaticEligible);

            if (approved is null)
            {
                return SystemImageRepairSafetyAssessment.Blocked(
                    "Sentinel found Windows integrity damage, but no repair action passed the automatic safety policy.");
            }

            if (approved.Action == SystemImageRepairAction.RestoreComponentStore &&
                !plan.Assessment.ComponentStoreCorruptionDetected)
            {
                return SystemImageRepairSafetyAssessment.Blocked(
                    "DISM repair was blocked because component-store corruption was not verified.");
            }

            if (approved.Action == SystemImageRepairAction.RepairProtectedFiles &&
                (!plan.Assessment.ProtectedFilesCorruptionDetected ||
                 plan.Assessment.ComponentStoreCorruptionDetected))
            {
                return SystemImageRepairSafetyAssessment.Blocked(
                    "SFC repair was blocked because the required integrity evidence or repair order was not verified.");
            }

            // DISM and SFC can run for a long time, consume substantial system
            // resources, and require elevation. Verified corruption evidence supports
            // a recommendation, but never constitutes approval to execute the repair.
            return SystemImageRepairSafetyAssessment.Blocked(
                approved.Action == SystemImageRepairAction.RestoreComponentStore
                    ? "Sentinel verified component-store corruption. DISM repair requires explicit user approval and will not run automatically."
                    : "Sentinel verified protected-file corruption. SFC repair requires explicit user approval and will not run automatically.");
        }
    }

    public sealed record SystemImageRepairSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        SystemImageRepairCandidate? ApprovedCandidate,
        bool RequiresElevation,
        string Summary)
    {
        public static SystemImageRepairSafetyAssessment Blocked(string summary) =>
            new(false, true, null, false, summary);
    }
}
