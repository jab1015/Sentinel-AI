/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Builds a compact, evidence-grounded local context for future Ask Sentinel
    /// responses. This layer intentionally exposes only values already present in
    /// the verified SystemSnapshot and never invents system state.
    /// </summary>
    public sealed class AskSentinelContextBuilder
    {
        public AskSentinelContext Build(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            List<string> evidence = new()
            {
                $"Snapshot time: {snapshot.Timestamp:O}",
                $"CPU usage: {snapshot.CpuUsagePercent:0.0}%",
                $"Memory usage: {snapshot.MemoryUsedGB:0.00} GB of {snapshot.MemoryTotalGB:0.00} GB ({snapshot.MemoryUsagePercent:0.0}%)",
                $"Memory pressure: {snapshot.MemoryPressureLevel}",
                $"Disk usage: {snapshot.DiskUsagePercent:0.0}%",
                $"Process monitoring: {(snapshot.ProcessMonitoringAvailable ? "Active" : "Unavailable")}",
                $"Processes: {snapshot.ProcessCount}; highest memory process: {snapshot.HighestMemoryProcessName} ({snapshot.HighestMemoryProcessGB:0.00} GB)",
                $"Defender: {snapshot.DefenderStatus}",
                $"Firewall: {snapshot.FirewallStatus}",
                $"Network connection monitoring: {(snapshot.NetworkConnectionMonitoringAvailable ? "Active" : "Unavailable")}",
                $"Connections: {snapshot.ExternalConnectionCount} external ({snapshot.InboundExternalConnectionCount} inbound, {snapshot.OutboundExternalConnectionCount} outbound); {snapshot.ListeningTcpEndpointCount} listening TCP; {snapshot.RepeatingExternalConnectionCount} external endpoints observed in at least three samples",
                $"Service monitoring: {(snapshot.ServiceMonitoringAvailable ? "Active" : "Unavailable")}; installed: {snapshot.InstalledServiceCount}; running: {snapshot.RunningServiceCount}; flagged: {snapshot.FlaggedServiceCount}",
                $"Process-lineage monitoring: {(snapshot.ProcessLineageMonitoringAvailable ? "Active" : "Unavailable")}; relationships: {snapshot.ProcessRelationshipCount}; flagged: {snapshot.FlaggedProcessRelationshipCount}",
                $"Command-line monitoring: {(snapshot.CommandLineMonitoringAvailable ? "Active" : "Unavailable")}; reviewed: {snapshot.ReviewedCommandLineProcessCount}; flagged: {snapshot.FlaggedCommandLineCount}",
                $"Startup persistence monitoring: {(snapshot.StartupPersistenceMonitoringAvailable ? "Active" : "Unavailable")}; entries: {snapshot.StartupEntryCount}; flagged: {snapshot.FlaggedStartupEntryCount}",
                $"Scheduled-task monitoring: {(snapshot.ScheduledTaskMonitoringAvailable ? "Active" : "Unavailable")}; tasks: {snapshot.ScheduledTaskCount}; flagged: {snapshot.FlaggedScheduledTaskCount}",
                $"Spyware correlation: {snapshot.SpywareCorrelationState}; confidence: {snapshot.SpywareCorrelationConfidenceScore}%; corroborating evidence: {snapshot.SpywareCorrelationHasCorroboratingEvidence}",
                $"Authentication monitoring: {(snapshot.AuthenticationMonitoringAvailable ? "Active" : "Unavailable")}",
                $"Authentication evidence: {snapshot.AuthenticationAnomalySummary}",
                $"Crash evidence monitoring: {(snapshot.CrashEvidenceAvailable ? "Active" : "Unavailable")}",
                $"Recent Windows crash evidence: {snapshot.RecentCrashSummary}",
                $"Investigation state: {snapshot.InvestigationState}",
                $"Investigation conclusion: {snapshot.InvestigationConclusion}",
                $"Investigation summary: {snapshot.InvestigationSummary}",
                $"Guidance title: {snapshot.GuidanceTitle}",
                $"Guidance confidence: {snapshot.GuidanceConfidencePercent}% ({snapshot.GuidanceConfidenceLabel})",
                $"Recommended action: {snapshot.GuidanceRecommendedAction}",
                $"Remediation available: {snapshot.RemediationAvailable}",
                $"Autonomous protection action: {snapshot.AutonomousProtectionAction}",
                $"Autonomous protection requires approval: {snapshot.AutonomousProtectionRequiresUserApproval}"
            };

            if (snapshot.FlaggedProcessCount > 0)
            {
                evidence.Add($"Flagged process: {snapshot.PrimaryFlaggedProcessName} — {snapshot.PrimaryFlaggedProcessReason}");
            }

            if (snapshot.FlaggedServiceCount > 0)
            {
                evidence.Add($"Flagged service: {snapshot.PrimaryFlaggedServiceName} — {snapshot.PrimaryFlaggedServiceReason}");
            }

            if (snapshot.FlaggedConnectionCount > 0)
            {
                evidence.Add($"Flagged connection: {snapshot.PrimaryFlaggedConnectionProcessName} -> {snapshot.PrimaryFlaggedConnectionRemoteEndpoint} — {snapshot.PrimaryFlaggedConnectionReason}");
            }

            if (snapshot.FlaggedStartupEntryCount > 0)
            {
                evidence.Add($"Flagged startup entry: {snapshot.PrimaryFlaggedStartupEntryName} — {snapshot.PrimaryFlaggedStartupEntryReason}");
            }

            if (snapshot.FlaggedScheduledTaskCount > 0)
            {
                evidence.Add($"Flagged scheduled task: {snapshot.PrimaryFlaggedScheduledTaskName} — {snapshot.PrimaryFlaggedScheduledTaskReason}");
            }

            if (!string.IsNullOrWhiteSpace(snapshot.MemoryTopContributors))
            {
                evidence.Add($"Memory contributors: {snapshot.MemoryTopContributors}");
            }

            return new AskSentinelContext(
                GeneratedAt: DateTimeOffset.Now,
                RequiresAttention: snapshot.InvestigationRequiresAttention,
                Evidence: evidence.AsReadOnly(),
                SafetyInstruction: "Answer only from the supplied Sentinel evidence. If the evidence does not establish an answer, say that Sentinel does not yet have enough verified information.");
        }

        public sealed record AskSentinelContext(
            DateTimeOffset GeneratedAt,
            bool RequiresAttention,
            IReadOnlyList<string> Evidence,
            string SafetyInstruction);
    }
}

