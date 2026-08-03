/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Produces one authoritative assessment for the dashboard, technical details,
    /// Ask Sentinel, notifications, history, repair decisions, and optimization.
    /// UI surfaces must not independently reinterpret raw evidence.
    /// </summary>
    public sealed class UnifiedInvestigationAssessmentService
    {
        public UnifiedInvestigationAssessment Evaluate(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            bool memoryRequiresAttention =
                snapshot.MemoryPressureLevel.Equals("High", StringComparison.OrdinalIgnoreCase);

            bool approvalRequired =
                snapshot.AutonomousProtectionRequiresUserApproval &&
                HasAction(snapshot.AutonomousProtectionAction) &&
                HasAction(snapshot.AutonomousProtectionTarget);

            bool requiresAttention =
                snapshot.InvestigationRequiresAttention ||
                memoryRequiresAttention ||
                approvalRequired;

            if (memoryRequiresAttention && !snapshot.InvestigationRequiresAttention && !approvalRequired)
            {
                return new UnifiedInvestigationAssessment(
                    State: UnifiedAssessmentState.Performance,
                    RequiresAttention: true,
                    ExecutiveTitle: "Sustained high memory use requires attention.",
                    ExecutiveSummary: snapshot.MemoryConclusion,
                    Recommendation: snapshot.MemoryRecommendation,
                    EvidenceSummary: $"Memory is at {snapshot.MemoryUsagePercent:0.0}%. Largest contributors: {snapshot.MemoryTopContributors}. Windows Memory Compression is using {snapshot.MemoryCompressionGB:0.00} GB.",
                    ActionId: "open-task-manager",
                    ActionLabel: "Review memory use",
                    ConfidencePercent: Math.Max(snapshot.GuidanceConfidencePercent, 80),
                    OptimizationEligible: true,
                    AutomaticChangeAllowed: false);
            }

            if (requiresAttention)
            {
                string title = FirstUseful(
                    snapshot.GuidanceTitle,
                    snapshot.InvestigationConclusion,
                    "Sentinel found a condition that requires attention.");

                string summary = FirstUseful(
                    snapshot.GuidanceWhatHappened,
                    snapshot.InvestigationSummary,
                    snapshot.RiskSummary,
                    "Sentinel found a condition that requires review.");

                string recommendation = approvalRequired
                    ? "A verified change is available, but Sentinel needs your approval before making it."
                    : FirstUseful(
                        snapshot.GuidanceRecommendedAction,
                        snapshot.Recommendation,
                        "Sentinel will continue monitoring while the condition is reviewed.");

                return new UnifiedInvestigationAssessment(
                    State: approvalRequired ? UnifiedAssessmentState.NeedsUser : UnifiedAssessmentState.Warning,
                    RequiresAttention: true,
                    ExecutiveTitle: title,
                    ExecutiveSummary: summary,
                    Recommendation: recommendation,
                    EvidenceSummary: FirstUseful(snapshot.GuidanceEvidence, snapshot.InvestigationSummary),
                    ActionId: approvalRequired ? "approve-remediation" : snapshot.GuidanceActionId,
                    ActionLabel: approvalRequired ? "Review recommended fix" : snapshot.GuidanceActionLabel,
                    ConfidencePercent: snapshot.GuidanceConfidencePercent,
                    OptimizationEligible: false,
                    AutomaticChangeAllowed: false);
            }

            bool reviewedAndCleared =
                snapshot.FlaggedProcessCount > 0 &&
                snapshot.GuidanceFixAvailability.Equals("No fix needed", StringComparison.OrdinalIgnoreCase) &&
                snapshot.GuidanceTitle.Contains("no security risk found", StringComparison.OrdinalIgnoreCase);

            string healthySummary = reviewedAndCleared
                ? "Sentinel completed the investigation and found no security risk."
                : "Sentinel reviewed the current evidence and found no condition requiring your attention.";

            return new UnifiedInvestigationAssessment(
                State: UnifiedAssessmentState.Healthy,
                RequiresAttention: false,
                ExecutiveTitle: "Your computer is healthy.",
                ExecutiveSummary: healthySummary,
                Recommendation: "No action is required. Sentinel will continue monitoring your computer.",
                EvidenceSummary: FirstUseful(snapshot.GuidanceEvidence, "Core protections and current system evidence were reviewed."),
                ActionId: string.Empty,
                ActionLabel: string.Empty,
                ConfidencePercent: Math.Max(snapshot.GuidanceConfidencePercent, 80),
                OptimizationEligible: true,
                AutomaticChangeAllowed: true);
        }

        private static bool HasAction(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            !value.Equals("None", StringComparison.OrdinalIgnoreCase);

        private static string FirstUseful(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) &&
                    !value.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !value.StartsWith("Loading", StringComparison.OrdinalIgnoreCase) &&
                    !value.StartsWith("Analyzing", StringComparison.OrdinalIgnoreCase))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }
    }

    public sealed record UnifiedInvestigationAssessment(
        UnifiedAssessmentState State,
        bool RequiresAttention,
        string ExecutiveTitle,
        string ExecutiveSummary,
        string Recommendation,
        string EvidenceSummary,
        string ActionId,
        string ActionLabel,
        int ConfidencePercent,
        bool OptimizationEligible,
        bool AutomaticChangeAllowed);

    public enum UnifiedAssessmentState
    {
        Healthy,
        Information,
        Performance,
        Warning,
        Security,
        Critical,
        Contained,
        Repairing,
        NeedsUser
    }
}
