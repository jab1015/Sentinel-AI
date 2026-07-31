/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts completed investigation evidence into a remediation recommendation.
    /// This component never changes the system. Execution remains isolated behind
    /// remediation policy and verified remediation services.
    /// </summary>
    public sealed class RemediationRecommendationEngine
    {
        public RemediationRecommendation Evaluate(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (!snapshot.InvestigationRequiresAttention)
            {
                return RemediationRecommendation.None(
                    "The investigation does not require action.");
            }

            if (!snapshot.DefenderEnabled)
            {
                return new RemediationRecommendation(
                    true,
                    true,
                    "restore-defender-protection",
                    "Microsoft Defender",
                    "Microsoft Defender protection is not enabled. Sentinel can guide a protected recovery action, but security settings must not be changed silently.");
            }

            if (!snapshot.FirewallEnabled)
            {
                return new RemediationRecommendation(
                    true,
                    true,
                    "restore-firewall-protection",
                    "Windows Firewall",
                    "Windows Firewall protection is not enabled. Sentinel can guide restoration, but firewall policy changes require explicit approval.");
            }

            if (snapshot.FlaggedConnectionCount > 0 &&
                HasValue(snapshot.PrimaryFlaggedConnectionRemoteEndpoint))
            {
                return new RemediationRecommendation(
                    true,
                    true,
                    "block-outbound-endpoint",
                    snapshot.PrimaryFlaggedConnectionRemoteEndpoint,
                    "The investigation identified an outbound network endpoint that may warrant blocking. Sentinel must obtain approval and verify the resulting firewall rule before reporting success.");
            }

            if (snapshot.FlaggedProcessCount > 0 &&
                HasValue(snapshot.PrimaryFlaggedProcessName))
            {
                return new RemediationRecommendation(
                    true,
                    true,
                    "contain-process",
                    snapshot.PrimaryFlaggedProcessName,
                    "The investigation identified a process that may require containment. Sentinel must obtain approval and re-verify the process state before reporting success.");
            }

            return RemediationRecommendation.None(
                "The investigation requires attention, but the current evidence does not justify a supported system-changing action.");
        }

        private static bool HasValue(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);

        public sealed record RemediationRecommendation(
            bool Available,
            bool RequiresUserApproval,
            string Action,
            string Target,
            string Summary)
        {
            public static RemediationRecommendation None(string summary) =>
                new(false, false, "None", "None", summary);
        }
    }
}
