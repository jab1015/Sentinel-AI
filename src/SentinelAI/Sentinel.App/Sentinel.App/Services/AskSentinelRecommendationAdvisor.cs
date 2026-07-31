/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Produces recommendation answers only from recommendation and remediation
    /// state already verified in the current Sentinel snapshot. It never promises
    /// an action, outcome, or threat conclusion that the snapshot does not establish.
    /// </summary>
    public sealed class AskSentinelRecommendationAdvisor
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to recommend an action for that question.";

        public RecommendationResult CreateRecommendation(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (!snapshot.InvestigationRequiresAttention)
            {
                return new RecommendationResult(
                    "No action is currently required based on Sentinel's verified evidence. Sentinel will continue monitoring your computer.",
                    IsSupported: true,
                    RequiresUserApproval: false,
                    CanExecuteAutonomously: false);
            }

            string recommendation = FirstVerifiedText(
                snapshot.GuidanceRecommendedAction,
                snapshot.Recommendation,
                snapshot.RemediationSummary);

            if (string.IsNullOrWhiteSpace(recommendation))
            {
                return new RecommendationResult(
                    InsufficientEvidence,
                    IsSupported: false,
                    RequiresUserApproval: false,
                    CanExecuteAutonomously: false);
            }

            bool requiresApproval = snapshot.RemediationRequiresUserApproval ||
                                    snapshot.AutonomousProtectionRequiresUserApproval;
            bool canExecuteAutonomously = snapshot.AutonomousProtectionCanExecute && !requiresApproval;

            string boundary;
            if (requiresApproval)
            {
                boundary = " Sentinel has not made this change because the verified remediation state requires your approval.";
            }
            else if (canExecuteAutonomously)
            {
                boundary = " Sentinel's current policy state allows this action to be handled autonomously when all execution-time safety checks still pass.";
            }
            else if (snapshot.RemediationAvailable)
            {
                boundary = " Sentinel has a verified remediation option available, but this answer does not claim that the change has been performed.";
            }
            else
            {
                boundary = " Sentinel does not currently report a verified automated remediation for this condition.";
            }

            string outcome = string.Empty;
            if (snapshot.RemediationAttempted || snapshot.AutonomousProtectionAttempted)
            {
                if (snapshot.RemediationSucceeded || snapshot.AutonomousProtectionSucceeded)
                {
                    outcome = " A prior action in the current snapshot is recorded as successful only because Sentinel has a verified success outcome.";
                }
                else
                {
                    outcome = " Sentinel does not report the attempted action as successful in the current verified snapshot.";
                }
            }

            return new RecommendationResult(
                recommendation.Trim() + boundary + outcome,
                IsSupported: true,
                RequiresUserApproval: requiresApproval,
                CanExecuteAutonomously: canExecuteAutonomously);
        }

        private static string FirstVerifiedText(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) &&
                    !value.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !value.Contains("waiting", StringComparison.OrdinalIgnoreCase) &&
                    !value.Contains("please wait", StringComparison.OrdinalIgnoreCase) &&
                    !value.Contains("analyzing", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        public sealed record RecommendationResult(
            string Answer,
            bool IsSupported,
            bool RequiresUserApproval,
            bool CanExecuteAutonomously);
    }
}
