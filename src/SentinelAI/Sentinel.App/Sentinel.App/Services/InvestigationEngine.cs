/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts verified technical monitoring evidence into one prioritized
    /// Sentinel Discovery outcome. Healthy evidence stays quiet; a user-facing
    /// alert requires a verified actionable condition or sufficiently
    /// corroborated evidence.
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

            bool defenderVerifiedInactive =
                snapshot.DefenderStatus.Equals("Disabled or inactive", StringComparison.OrdinalIgnoreCase) ||
                snapshot.DefenderStatus.Equals("Limited", StringComparison.OrdinalIgnoreCase) ||
                snapshot.DefenderStatus.Equals("Not detected", StringComparison.OrdinalIgnoreCase);
            bool firewallVerifiedInactive =
                snapshot.FirewallStatus.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                snapshot.FirewallStatus.StartsWith("Partial", StringComparison.OrdinalIgnoreCase);
            bool securityProtectionDisabled = defenderVerifiedInactive || firewallVerifiedInactive;
            bool basicTierHealthy =
                Contains(snapshot.ProtectionHealthReasonCode, "basic-protection-healthy-subscription-required");
            bool protectionHealthDegraded =
                !snapshot.ProtectionHealthFullyProtected &&
                !basicTierHealthy &&
                !Contains(snapshot.ProtectionHealthState, "Starting");

            bool highMemoryPressure = snapshot.MemoryPressureLevel.Equals("High", StringComparison.OrdinalIgnoreCase);
            bool criticallyLowDisk = snapshot.DiskTotalGB > 0 &&
                (snapshot.DiskUsagePercent >= 95 || snapshot.DiskFreeGB <= 5);

            bool suspiciousProcess = snapshot.FlaggedProcessCount > 0;
            bool unusualProcessLineage = snapshot.FlaggedProcessRelationshipCount > 0;
            bool unusualCommandLine = snapshot.FlaggedCommandLineCount > 0;
            bool suspiciousStartupPersistence = snapshot.FlaggedStartupEntryCount > 0;
            bool suspiciousScheduledTask = snapshot.FlaggedScheduledTaskCount > 0;
            bool unusualConnection = snapshot.FlaggedConnectionCount > 0;

            bool highConfidenceNetworkFinding =
                snapshot.ConnectionIntelligenceConfidenceScore >= 80 &&
                snapshot.ConnectionIntelligenceHasCorroboratingEvidence &&
                !Contains(snapshot.ConnectionIntelligenceState, "Normal") &&
                !Contains(snapshot.ConnectionIntelligenceState, "Starting");

            bool highConfidenceSpywareFinding =
                snapshot.SpywareCorrelationConfidenceScore >= 80 &&
                snapshot.SpywareCorrelationHasCorroboratingEvidence &&
                !Contains(snapshot.SpywareCorrelationState, "Normal") &&
                !Contains(snapshot.SpywareCorrelationState, "Starting");

            bool connectionCorrelatesWithProcess = unusualConnection && suspiciousProcess &&
                SameName(snapshot.PrimaryFlaggedConnectionProcessName, snapshot.PrimaryFlaggedProcessName);
            bool lineageCorrelatesWithProcess = unusualProcessLineage && suspiciousProcess &&
                SameName(snapshot.PrimaryLineageChildProcessName, snapshot.PrimaryFlaggedProcessName);
            bool lineageCorrelatesWithConnection = unusualProcessLineage && unusualConnection &&
                SameName(snapshot.PrimaryLineageChildProcessName, snapshot.PrimaryFlaggedConnectionProcessName);
            bool commandLineCorrelatesWithProcess = unusualCommandLine && suspiciousProcess &&
                SameName(snapshot.PrimaryCommandLineProcessName, snapshot.PrimaryFlaggedProcessName);
            bool commandLineCorrelatesWithLineage = unusualCommandLine && unusualProcessLineage &&
                SameName(snapshot.PrimaryCommandLineProcessName, snapshot.PrimaryLineageChildProcessName);
            bool commandLineCorrelatesWithConnection = unusualCommandLine && unusualConnection &&
                SameName(snapshot.PrimaryCommandLineProcessName, snapshot.PrimaryFlaggedConnectionProcessName);

            bool serviceFailure = !storageSpacesSmp &&
                Contains(snapshot.LatestEventSource, "Service Control Manager") &&
                Contains(snapshot.LatestEventMessage, "terminated unexpectedly");
            bool actionableSystemEvidence = !storageSpacesSmp && snapshot.FlaggedServiceCount > 0;

            if (snapshot.AuthenticationAnomalyDetected && snapshot.AuthenticationAnomalyConfidenceScore >= 65)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Repeated failed logons require investigation.",
                    snapshot.AuthenticationAnomalySummary,
                    true,
                    "authentication-brute-force-pattern");
            }

            if (securityProtectionDisabled || protectionHealthDegraded)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Windows protection requires attention.",
                    securityProtectionDisabled
                        ? "Sentinel verified that a core Windows security protection is not enabled."
                        : snapshot.ProtectionHealthSummary,
                    true,
                    securityProtectionDisabled
                        ? "security-protection-disabled"
                        : "security-monitoring-coverage-degraded");
            }

            if (highConfidenceSpywareFinding)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Sentinel found corroborated spyware indicators.",
                    snapshot.SpywareCorrelationSummary,
                    true,
                    "corroborated-spyware-finding");
            }

            if (highConfidenceNetworkFinding)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Sentinel found suspicious network activity.",
                    snapshot.ConnectionIntelligenceSummary,
                    true,
                    "corroborated-network-finding");
            }

            if (commandLineCorrelatesWithProcess || commandLineCorrelatesWithLineage || commandLineCorrelatesWithConnection)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Correlated process behavior requires attention.",
                    $"{snapshot.PrimaryCommandLineProcessName}: {snapshot.PrimaryCommandLineReason}",
                    true,
                    commandLineCorrelatesWithConnection ? "correlated-command-network-finding" :
                    commandLineCorrelatesWithLineage ? "correlated-command-lineage-finding" :
                    "correlated-command-process-finding");
            }

            if (lineageCorrelatesWithProcess || lineageCorrelatesWithConnection)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Related process activity requires attention.",
                    $"{snapshot.PrimaryLineageParentProcessName} started {snapshot.PrimaryLineageChildProcessName}. {snapshot.PrimaryLineageReason}",
                    true,
                    lineageCorrelatesWithConnection ? "correlated-lineage-network-finding" : "correlated-lineage-process-finding");
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

            if (highMemoryPressure)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Memory use requires attention.",
                    snapshot.MemoryConclusion,
                    true,
                    "high-memory-pressure");
            }

            if (criticallyLowDisk)
            {
                return new InvestigationResult(
                    InvestigationState.ActionRequired,
                    "Your system drive is running low on space.",
                    $"Only {snapshot.DiskFreeGB:0.0} GB is free. Sentinel should help identify safe cleanup options before Windows runs out of working space.",
                    true,
                    "critical-disk-space");
            }

            // Single uncorroborated indicators remain in Discovery without alarming
            // the user. Sentinel continues collecting evidence and escalates when
            // corroboration or a clearly actionable condition appears.
            if (unusualCommandLine)
                return Investigating("Sentinel is investigating command activity.", $"{snapshot.PrimaryCommandLineProcessName}: {snapshot.PrimaryCommandLineReason}", "command-line-under-review");

            if (unusualProcessLineage)
                return Investigating("Sentinel is investigating related process activity.", $"{snapshot.PrimaryLineageParentProcessName} started {snapshot.PrimaryLineageChildProcessName}.", "process-lineage-under-review");

            if (suspiciousProcess)
                return Investigating("Sentinel is investigating a running process.", $"{snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}", "process-evidence-under-review");

            if (suspiciousStartupPersistence)
                return Investigating("Sentinel is investigating a startup item.", $"{snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}", "startup-persistence-under-review");

            if (suspiciousScheduledTask)
                return Investigating("Sentinel is investigating a scheduled task.", $"{snapshot.PrimaryFlaggedScheduledTaskName}: {snapshot.PrimaryFlaggedScheduledTaskReason}", "scheduled-task-under-review");

            if (unusualConnection)
                return Investigating("Sentinel is investigating network activity.", $"{snapshot.PrimaryFlaggedConnectionProcessName} connected to {snapshot.PrimaryFlaggedConnectionRemoteEndpoint}.", "network-evidence-under-review");

            return new InvestigationResult(
                InvestigationState.NoIssue,
                "Your computer is healthy.",
                "Nothing requires your attention right now.",
                false,
                "healthy");
        }

        private static InvestigationResult Investigating(string conclusion, string evidence, string reasonCode) =>
            new(
                InvestigationState.Investigating,
                conclusion,
                evidence + " Sentinel is continuing to verify this quietly; no user action is required unless the evidence becomes actionable.",
                false,
                reasonCode);

        private static bool SameName(string? first, string? second) =>
            !string.IsNullOrWhiteSpace(first) &&
            !string.IsNullOrWhiteSpace(second) &&
            first.Equals(second, StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string? value, string text) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains(text, StringComparison.OrdinalIgnoreCase);

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
