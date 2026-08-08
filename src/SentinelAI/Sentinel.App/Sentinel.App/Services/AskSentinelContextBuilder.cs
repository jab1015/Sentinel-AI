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
                $"Processes: {snapshot.ProcessCount}; highest memory process: {snapshot.HighestMemoryProcessName} ({snapshot.HighestMemoryProcessGB:0.00} GB)",
                $"Defender: {snapshot.DefenderStatus}",
                $"Firewall: {snapshot.FirewallStatus}",
                $"Authentication monitoring: {(snapshot.AuthenticationMonitoringAvailable ? "Active" : "Unavailable")}",
                $"Authentication evidence: {snapshot.AuthenticationAnomalySummary}",
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
