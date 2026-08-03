/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final safety gate before any automatic network repair. Low-risk DNS cache
    /// repair may be allowed when evidence supports it; disruptive resets remain
    /// blocked unless a future policy explicitly verifies stronger conditions.
    /// </summary>
    public sealed class NetworkRepairSafetyService
    {
        public NetworkRepairSafetyAssessment Evaluate(
            NetworkRepairPlan plan,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return NetworkRepairSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!plan.ActionWarranted || plan.Candidates.Count == 0)
            {
                return NetworkRepairSafetyAssessment.Blocked(plan.Summary);
            }

            NetworkRepairCandidate? approved = plan.Candidates
                .FirstOrDefault(candidate =>
                    candidate.AutomaticEligible &&
                    candidate.Action == NetworkRepairAction.FlushDnsCache);

            if (approved is null)
            {
                return NetworkRepairSafetyAssessment.Blocked(
                    "Sentinel found a network condition, but no automatic repair passed the safety policy.");
            }

            return new NetworkRepairSafetyAssessment(
                true,
                settings.VerifyEveryChange,
                approved,
                "A low-risk DNS cache repair passed Sentinel's automatic network-repair policy.");
        }
    }

    public sealed record NetworkRepairSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        NetworkRepairCandidate? ApprovedCandidate,
        string Summary)
    {
        public static NetworkRepairSafetyAssessment Blocked(string summary) =>
            new(false, true, null, summary);
    }
}
