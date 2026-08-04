/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final safety decision for measured startup optimization. A startup item can
    /// reach automatic-change eligibility only when sustained boot regression and
    /// repeated Windows-attributed degradation are both verified.
    /// </summary>
    public sealed class BootStartupOptimizationSafetyService
    {
        public BootStartupOptimizationSafetyAssessment Evaluate(
            BootStartupImpactCorrelation correlation,
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(correlation);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.AutomaticOptimizationEnabled)
            {
                return BootStartupOptimizationSafetyAssessment.Blocked(
                    "Automatic optimization is turned off.");
            }

            if (!correlation.BootHistory.SustainedRegressionDetected ||
                !correlation.VerifiedStartupCauseFound ||
                correlation.VerifiedItems.Count == 0)
            {
                return BootStartupOptimizationSafetyAssessment.Blocked(correlation.Summary);
            }

            CorrelatedStartupImpact? approved = correlation.VerifiedItems
                .Where(item =>
                    item.RepeatedImpactVerified &&
                    item.MaximumDegradationTimeMs >= 1500 &&
                    item.Candidate.WorkingSetBytes >= 150L * 1024 * 1024 &&
                    !item.Candidate.Name.Contains("Sentinel", StringComparison.OrdinalIgnoreCase) &&
                    !item.Candidate.Command.Contains("Sentinel", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.MaximumDegradationTimeMs)
                .FirstOrDefault();

            if (approved is null)
            {
                return BootStartupOptimizationSafetyAssessment.Blocked(
                    "Sentinel verified startup slowdown, but no individual startup item passed the final automatic-change safety threshold.");
            }

            // Conservative mode permits only the single highest-confidence startup
            // candidate per verified maintenance cycle. The change executor must
            // preserve the original registry value for rollback before disabling it.
            return new BootStartupOptimizationSafetyAssessment(
                true,
                settings.VerifyEveryChange,
                settings.RollBackUnsuccessfulChanges,
                approved,
                "One startup item passed Sentinel's measured boot-impact safety policy. Any change must be reversible and verified after execution.");
        }
    }

    public sealed record BootStartupOptimizationSafetyAssessment(
        bool ExecutionAllowed,
        bool VerificationRequired,
        bool RollbackRequired,
        CorrelatedStartupImpact? ApprovedItem,
        string Summary)
    {
        public static BootStartupOptimizationSafetyAssessment Blocked(string summary) =>
            new(false, true, true, null, summary);
    }
}
