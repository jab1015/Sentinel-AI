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
                    false,
                    "refresh-security-state",
                    "Microsoft Defender",
                    "Sentinel can safely refresh the current Windows security state automatically before deciding whether user action is required.");
            }

            if (!snapshot.FirewallEnabled)
            {
                return new RemediationRecommendation(
                    true,
                    false,
                    "refresh-security-state",
                    "Windows Firewall",
                    "Sentinel can safely refresh the current Windows security state automatically before deciding whether user action is required.");
            }

            if (IsTransientWindowsUpdateCondition(snapshot))
            {
                return new RemediationRecommendation(
                    true,
                    false,
                    "retry-transient-operation",
                    "Windows Update",
                    "Sentinel can safely refresh the transient Windows Update evidence automatically and continue monitoring for recurrence.");
            }

            // A connection on an uncommon port is evidence for review, not proof of
            // malicious activity. Only offer a system-changing network block when the
            // Investigation Engine has independently correlated the connection with
            // another process signal and promoted it to an actionable finding.
            if (IsVerifiedNetworkFinding(snapshot) &&
                snapshot.FlaggedConnectionCount > 0 &&
                HasValue(snapshot.PrimaryFlaggedConnectionRemoteEndpoint))
            {
                return new RemediationRecommendation(
                    true,
                    true,
                    "block-outbound-endpoint",
                    snapshot.PrimaryFlaggedConnectionRemoteEndpoint,
                    "Sentinel correlated this network endpoint with additional process evidence. Approval is required before blocking it, and Sentinel will verify the resulting firewall rule before reporting success.");
            }

            if (snapshot.FlaggedProcessCount > 0 &&
                HasValue(snapshot.PrimaryFlaggedProcessName) &&
                IsVerifiedProcessFinding(snapshot))
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

        private static bool IsVerifiedNetworkFinding(SystemSnapshot snapshot) =>
            snapshot.InvestigationReasonCode is
                "correlated-process-network-finding" or
                "correlated-lineage-network-finding" or
                "correlated-command-network-finding";

        private static bool IsVerifiedProcessFinding(SystemSnapshot snapshot) =>
            snapshot.InvestigationReasonCode is
                "correlated-command-process-finding" or
                "correlated-command-lineage-finding" or
                "correlated-lineage-process-finding";

        private static bool IsTransientWindowsUpdateCondition(SystemSnapshot snapshot) =>
            Contains(snapshot.LatestEventSource, "WindowsUpdateClient") &&
            Contains(snapshot.LatestEventMessage, "0x80073D02");

        private static bool Contains(string? value, string text) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(text, StringComparison.OrdinalIgnoreCase);

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
