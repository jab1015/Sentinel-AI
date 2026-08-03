/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts verified performance-baseline evidence into conservative optimization
    /// decisions. This layer decides whether an optimization should be considered;
    /// it does not make Windows changes itself.
    /// </summary>
    public sealed class OptimizationDecisionService
    {
        public OptimizationDecision Evaluate(
            PerformanceBaselineService.PerformanceBaselineResult baseline,
            UnifiedInvestigationAssessment assessment)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            ArgumentNullException.ThrowIfNull(assessment);

            if (assessment.RequiresAttention || !assessment.OptimizationEligible)
            {
                return OptimizationDecision.None(
                    "Optimization is deferred while Sentinel is investigating a condition that requires attention.");
            }

            if (!baseline.IsEstablished)
            {
                return OptimizationDecision.None(baseline.Summary);
            }

            var candidates = new List<OptimizationCandidate>();

            if (baseline.CpuDeviation)
            {
                candidates.Add(new OptimizationCandidate(
                    OptimizationKind.CpuPressure,
                    "Investigate sustained CPU pressure",
                    "CPU use is materially above this computer's established baseline.",
                    OptimizationRisk.Low,
                    false));
            }

            if (baseline.MemoryDeviation)
            {
                candidates.Add(new OptimizationCandidate(
                    OptimizationKind.MemoryPressure,
                    "Investigate sustained memory pressure",
                    "Memory use is materially above this computer's established baseline.",
                    OptimizationRisk.Low,
                    false));
            }

            if (baseline.ProcessCountDeviation)
            {
                candidates.Add(new OptimizationCandidate(
                    OptimizationKind.ProcessPressure,
                    "Review abnormal background process growth",
                    "The number of running processes is materially above this computer's established baseline.",
                    OptimizationRisk.Low,
                    false));
            }

            if (baseline.DiskPressure)
            {
                candidates.Add(new OptimizationCandidate(
                    OptimizationKind.StoragePressure,
                    "Recover safe temporary storage",
                    "Storage usage is at or above 90 percent.",
                    OptimizationRisk.Low,
                    true));
            }

            if (candidates.Count == 0)
            {
                return OptimizationDecision.None(
                    "No verified optimization is currently warranted. Performance is within the established baseline.");
            }

            bool automaticAllowed =
                assessment.AutomaticChangeAllowed &&
                candidates.TrueForAll(candidate => candidate.Risk == OptimizationRisk.Low && candidate.AutomaticEligible);

            return new OptimizationDecision(
                true,
                automaticAllowed,
                candidates,
                automaticAllowed
                    ? "A low-risk optimization is supported by verified local evidence."
                    : "Sentinel detected a performance deviation and will investigate the cause before making a change.");
        }
    }

    public sealed record OptimizationDecision(
        bool OptimizationWarranted,
        bool AutomaticChangeAllowed,
        IReadOnlyList<OptimizationCandidate> Candidates,
        string Summary)
    {
        public static OptimizationDecision None(string summary) =>
            new(false, false, Array.Empty<OptimizationCandidate>(), summary);
    }

    public sealed record OptimizationCandidate(
        OptimizationKind Kind,
        string Title,
        string Evidence,
        OptimizationRisk Risk,
        bool AutomaticEligible);

    public enum OptimizationKind
    {
        CpuPressure,
        MemoryPressure,
        ProcessPressure,
        StoragePressure
    }

    public enum OptimizationRisk
    {
        Low,
        Moderate,
        High
    }
}
