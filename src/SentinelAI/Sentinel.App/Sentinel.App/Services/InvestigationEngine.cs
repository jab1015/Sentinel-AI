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
            bool unusualProcessLineage = snapshot.FlaggedProcessRelationshipCount > 0;
            bool unusualCommandLine = snapshot.FlaggedCommandLineCount > 0;
            bool suspiciousStartupPersistence = snapshot.FlaggedStartupEntryCount > 0;
            bool suspiciousScheduledTask = snapshot.FlaggedScheduledTaskCount > 0;
            bool unusualConnection = snapshot.FlaggedConnectionCount > 0;

            bool connectionCorrelatesWithProcess =
                unusualConnection &&
                suspiciousProcess &&
                SameName(snapshot.PrimaryFlaggedConnectionProcessName, snapshot.PrimaryFlaggedProcessName);

            bool lineageCorrelatesWithProcess =
                unusualProcessLineage &&
                suspiciousProcess &&
                SameName(snapshot.PrimaryLineageChildProcessName, snapshot.PrimaryFlaggedProcessName);

            bool lineageCorrelatesWithConnection =
                unusualProcessLineage &&
                unusualConnection &&
                SameName(snapshot.PrimaryLineageChildProcessName, snapshot.PrimaryFlaggedConnectionProcessName);

            bool commandLineCorrelatesWithProcess =
                unusualCommandLine &&
                suspiciousProcess &&
                SameName(snapshot.PrimaryCommandLineProcessName, snapshot.PrimaryFlaggedProcessName);

            bool commandLineCorrelatesWithLineage =
                unusualCommandLine &&
                unusualProcessLineage &&
                SameName(snapshot.PrimaryCommandLineProcessName, snapshot.PrimaryLineageChildProcessName);

            bool commandLineCorrelatesWithConnection =
                unusualCommandLine &&
                unusualConnection &&
                SameName(snapshot.PrimaryCommandLineProcessName, snapshot.PrimaryFlaggedConnectionProcessName);

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

            if (commandLineCorrelatesWithProcess ||
                commandLineCorrelatesWithLineage ||
                commandLineCorrelatesWithConnection)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Correlated process behavior requires attention.",
                    $"{snapshot.PrimaryCommandLineProcessName}: {snapshot.PrimaryCommandLineReason}",
                    true,
                    commandLineCorrelatesWithConnection
                        ? "correlated-command-network-finding"
                        : commandLineCorrelatesWithLineage
                            ? "correlated-command-lineage-finding"
                            : "correlated-command-process-finding");
            }

            if (lineageCorrelatesWithProcess || lineageCorrelatesWithConnection)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Related process activity requires attention.",
                    $"{snapshot.PrimaryLineageParentProcessName} started {snapshot.PrimaryLineageChildProcessName}. {snapshot.PrimaryLineageReason}",
                    true,
                    lineageCorrelatesWithConnection
                        ? "correlated-lineage-network-finding"
                        : "correlated-lineage-process-finding");
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

            if (serviceFailure || actionableSystemEvidence)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "A Windows condition requires attention.",
                    snapshot.GuidanceWhatHappened,
                    true,
                    serviceFailure ? "service-failure" : "system-finding");
            }

            if (unusualCommandLine)
            {
                return new InvestigationResult(
                    InvestigationState.Investigating,
                    "Sentinel is investigating command activity.",
                    $"{snapshot.PrimaryCommandLineProcessName}: {snapshot.PrimaryCommandLineReason} No action is required unless another signal confirms a risk.",
                    false,
                    "command-line-under-review");
            }

            if (unusualProcessLineage)
            {
                return new InvestigationResult(
                    InvestigationState.Investigating,
                    "Sentinel is investigating related process activity.",
                    $"{snapshot.PrimaryLineageParentProcessName} started {snapshot.PrimaryLineageChildProcessName}. No action is required unless another signal confirms a risk.",
                    false,
                    "process-lineage-under-review");
            }

            if (suspiciousProcess)
            {
                return new InvestigationResult(
                    InvestigationState.Investigating,
                    "Sentinel is investigating a running process.",
                    $"{snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason} No action is required unless another signal confirms a risk.",
                    false,
                    "process-evidence-under-review");
            }

            if (suspiciousStartupPersistence)
            {
                return new InvestigationResult(
                    InvestigationState.Investigating,
                    "Sentinel is investigating a startup item.",
                    $"{snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason} No action is required unless another signal confirms a risk.",
                    false,
                    "startup-persistence-under-review");
            }

            if (suspiciousScheduledTask)
            {
                return new InvestigationResult(
                    InvestigationState.Investigating,
                    "Sentinel is investigating a scheduled task.",
                    $"{snapshot.PrimaryFlaggedScheduledTaskName}: {snapshot.PrimaryFlaggedScheduledTaskReason} No action is required unless another signal confirms a risk.",
                    false,
                    "scheduled-task-under-review");
            }

            if (unusualConnection)
            {
                return new InvestigationResult(
                    InvestigationState.Investigating,
                    "Sentinel is investigating network activity.",
                    $"{snapshot.PrimaryFlaggedConnectionProcessName} connected to {snapshot.PrimaryFlaggedConnectionRemoteEndpoint}. No action is required unless another signal confirms a risk.",
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
