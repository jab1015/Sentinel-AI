/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts raw monitoring evidence into one user-facing investigation outcome.
    /// Sentinel reports conclusions instead of individual Windows events.
    /// </summary>
    public sealed class InvestigationEngine
    {
        public InvestigationResult Investigate(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            bool storageSpacesSmp =
                Contains(snapshot.LatestEventMessage, "Storage Spaces SMP") ||
                Contains(snapshot.LatestEventMessage, "Microsoft Storage Spaces SMP") ||
                Contains(snapshot.PrimaryFlaggedServiceName, "Storage Spaces") ||
                Contains(snapshot.PrimaryFlaggedServiceName, "SMP");

            bool securityProtectionDisabled =
                !snapshot.DefenderEnabled || !snapshot.FirewallEnabled;

            bool suspiciousProcess = snapshot.FlaggedProcessCount > 0;
            bool suspiciousStartupPersistence = snapshot.FlaggedStartupEntryCount > 0;

            bool serviceFailure =
                !storageSpacesSmp &&
                Contains(snapshot.LatestEventSource, "Service Control Manager") &&
                Contains(snapshot.LatestEventMessage, "terminated unexpectedly");

            bool actionableSystemEvidence =
                !storageSpacesSmp &&
                (snapshot.FlaggedServiceCount > 0 ||
                 snapshot.CriticalEventCount > 0 ||
                 snapshot.ErrorEventCount > 0 ||
                 snapshot.RiskScore >= 20);

            if (securityProtectionDisabled)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Windows protection requires attention.",
                    "Sentinel verified that a core Windows security protection is not enabled.",
                    true,
                    "security-protection-disabled");
            }

            if (suspiciousProcess)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "A running process requires attention.",
                    snapshot.PrimaryFlaggedProcessReason,
                    true,
                    "process-finding");
            }

            if (suspiciousStartupPersistence)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "A startup item requires attention.",
                    $"{snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}",
                    true,
                    "startup-persistence-finding");
            }

            if (serviceFailure || actionableSystemEvidence)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "A Windows condition requires attention.",
                    snapshot.GuidanceWhatHappened,
                    true,
                    serviceFailure ? "service-failure" : "system-finding");
            }

            return new InvestigationResult(
                InvestigationState.NoIssue,
                "Your computer is healthy.",
                "Nothing requires your attention right now.",
                false,
                "healthy");
        }

        private static bool Contains(string? value, string text) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(text, StringComparison.OrdinalIgnoreCase);

        public enum InvestigationState
        {
            NoIssue,
            Investigating,
            ActionRequired
        }

        public sealed record InvestigationResult(
            InvestigationState State,
            string Conclusion,
            string Summary,
            bool RequiresAttention,
            string ReasonCode);
    }
}
