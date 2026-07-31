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
            bool suspiciousScheduledTask = snapshot.FlaggedScheduledTaskCount > 0;
            bool unusualConnection = snapshot.FlaggedConnectionCount > 0;

            bool connectionCorrelatesWithProcess =
                unusualConnection &&
                suspiciousProcess &&
                SameName(
                    snapshot.PrimaryFlaggedConnectionProcessName,
                    snapshot.PrimaryFlaggedProcessName);

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

            if (connectionCorrelatesWithProcess)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "A running process and its network activity require attention.",
                    $"{snapshot.PrimaryFlaggedProcessName} has a process warning and is connected to {snapshot.PrimaryFlaggedConnectionRemoteEndpoint}. {snapshot.PrimaryFlaggedConnectionReason}",
                    true,
                    "correlated-process-network-finding");
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

            if (suspiciousScheduledTask)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "A scheduled task requires attention.",
                    $"{snapshot.PrimaryFlaggedScheduledTaskName}: {snapshot.PrimaryFlaggedScheduledTaskReason}",
                    true,
                    "scheduled-task-finding");
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

            if (unusualConnection)
            {
                return new InvestigationResult(
                    InvestigationState.Investigating,
                    "Sentinel is investigating network activity.",
                    $"{snapshot.PrimaryFlaggedConnectionProcessName} connected to {snapshot.PrimaryFlaggedConnectionRemoteEndpoint}. This does not require your attention unless other evidence confirms a risk.",
                    false,
                    "network-evidence-under-review");
            }

            return new InvestigationResult(
                InvestigationState.NoIssue,
                "Your computer is healthy.",
                "Nothing requires your attention right now.",
                false,
                "healthy");
        }

        private static bool SameName(string? first, string? second) =>
            !string.IsNullOrWhiteSpace(first) &&
            !string.IsNullOrWhiteSpace(second) &&
            first.Equals(second, StringComparison.OrdinalIgnoreCase);

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
