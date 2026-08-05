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
                return RemediationRecommendation.None("The investigation does not require action.");

            if (!snapshot.DefenderEnabled)
                return Automatic("refresh-security-state", "Microsoft Defender", "Sentinel can safely refresh the current Windows security state automatically before deciding whether user action is required.");

            if (!snapshot.FirewallEnabled)
                return Automatic("refresh-security-state", "Windows Firewall", "Sentinel can safely refresh the current Windows security state automatically before deciding whether user action is required.");

            if (IsTransientWindowsUpdateCondition(snapshot))
                return Automatic("retry-transient-operation", "Windows Update", "Sentinel can safely refresh the transient Windows Update evidence automatically and continue monitoring for recurrence.");

            if (snapshot.InvestigationReasonCode.StartsWith("driver:", StringComparison.OrdinalIgnoreCase))
                return Guided("review-driver-repair", snapshot.GuidanceTitle, "Sentinel can investigate the exact signed driver and prepare a verified repair. Driver installation and any restart remain approval-gated.");

            switch (snapshot.InvestigationReasonCode)
            {
                case "windows-restart-pending":
                    return Guided("review-windows-restart", "Windows restart", "Windows is waiting for a restart. Sentinel will not restart the computer automatically; the user must save work and approve the restart.");
                case "windows-updates-pending":
                    return Guided("open-windows-update", "Windows Update", "Sentinel verified pending Windows updates. General update installation remains user-approved through Windows Update.");
                case "secure-boot-disabled":
                    return Guided("review-secure-boot", "Secure Boot", "Secure Boot requires firmware-level review. Sentinel will explain the condition but will not alter UEFI/BIOS settings automatically.");
                case "tpm-not-ready":
                    return Guided("review-tpm", "TPM", "TPM configuration can require firmware changes. Sentinel will explain the condition but will not alter firmware security settings automatically.");
                case "memory-pressure-high":
                    return Guided("open-task-manager", "Memory use", "Sentinel can open Task Manager so the user can review high-memory applications before closing anything.");
                case "disk-space-critical":
                    return Guided("open-storage", "System drive", "Sentinel can open Windows Storage settings. It will not delete personal files automatically.");
                case "service-failure":
                case "system-finding":
                    return Guided("open-services", snapshot.PrimaryFlaggedServiceName, "Sentinel can open Windows Services for review. Restarting or disabling a service requires verification and approval.");
            }

            if (IsVerifiedNetworkFinding(snapshot) && snapshot.FlaggedConnectionCount > 0 && HasValue(snapshot.PrimaryFlaggedConnectionRemoteEndpoint))
            {
                return new RemediationRecommendation(
                    true,
                    true,
                    "block-outbound-endpoint",
                    snapshot.PrimaryFlaggedConnectionRemoteEndpoint,
                    "Sentinel correlated this network endpoint with additional process evidence. Approval is required before blocking it, and Sentinel will verify the resulting firewall rule before reporting success.",
                    RemediationDisposition.ApprovalRequired);
            }

            if (snapshot.FlaggedProcessCount > 0 && HasValue(snapshot.PrimaryFlaggedProcessName) && IsVerifiedProcessFinding(snapshot))
            {
                return new RemediationRecommendation(
                    true,
                    true,
                    "contain-process",
                    snapshot.PrimaryFlaggedProcessName,
                    "The investigation identified a process that may require containment. Sentinel must obtain approval and re-verify the process state before reporting success.",
                    RemediationDisposition.ApprovalRequired);
            }

            return new RemediationRecommendation(
                false,
                false,
                "observe-only",
                "None",
                "Sentinel verified a condition that requires attention, but the current evidence does not justify a supported system-changing action. Sentinel will continue investigating and monitoring it.",
                RemediationDisposition.ObserveOnly);
        }

        private static RemediationRecommendation Automatic(string action, string target, string summary) =>
            new(true, false, action, target, summary, RemediationDisposition.SafeAutomatic);

        private static RemediationRecommendation Guided(string action, string target, string summary) =>
            new(true, true, action, string.IsNullOrWhiteSpace(target) ? "Windows" : target, summary, RemediationDisposition.GuidedUserAction);

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
            !string.IsNullOrWhiteSpace(value) && value.Contains(text, StringComparison.OrdinalIgnoreCase);

        private static bool HasValue(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);

        public enum RemediationDisposition
        {
            None,
            SafeAutomatic,
            ApprovalRequired,
            GuidedUserAction,
            ObserveOnly
        }

        public sealed record RemediationRecommendation(
            bool Available,
            bool RequiresUserApproval,
            string Action,
            string Target,
            string Summary,
            RemediationDisposition Disposition)
        {
            public static RemediationRecommendation None(string summary) =>
                new(false, false, "None", "None", summary, RemediationDisposition.None);
        }
    }
}
