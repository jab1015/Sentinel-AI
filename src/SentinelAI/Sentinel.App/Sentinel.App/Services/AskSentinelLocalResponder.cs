/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Provides immediate, strictly evidence-grounded answers for Ask Sentinel.
    /// Unsupported questions fail closed instead of inferring facts.
    /// </summary>
    public sealed class AskSentinelLocalResponder
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to answer that question.";

        public string Answer(string question, SystemSnapshot snapshot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);

            string normalized = question.Trim().ToLowerInvariant();

            if (ContainsAny(normalized, "windows update", "updates", "update status"))
            {
                return "Sentinel does not yet have verified Windows Update evidence for this question.";
            }

            if (ContainsAny(normalized, "pending restart", "restart required", "reboot required", "need to restart"))
            {
                return "Sentinel does not yet have verified pending-restart evidence for this question.";
            }

            if (ContainsAny(normalized, "tpm", "trusted platform module"))
            {
                return "Sentinel does not yet have verified TPM evidence for this question.";
            }

            if (ContainsAny(normalized, "secure boot"))
            {
                return "Sentinel does not yet have verified Secure Boot evidence for this question.";
            }

            if (ContainsAny(normalized, "bitlocker", "device encryption", "drive encryption"))
            {
                return "Sentinel does not yet have verified BitLocker or device-encryption evidence for this question.";
            }

            if (ContainsAny(normalized, "healthy", "health", "overall status", "anything wrong", "problem", "attention"))
            {
                return snapshot.InvestigationRequiresAttention
                    ? $"Sentinel currently has a verified condition that requires attention. {Safe(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened)}"
                    : $"Sentinel's current verified evidence does not show a condition that requires your attention. CPU is {snapshot.CpuUsagePercent:0.0}%, memory is {snapshot.MemoryUsagePercent:0.0}%, disk use is {snapshot.DiskUsagePercent:0.0}%, Defender is {snapshot.DefenderStatus}, and Firewall is {snapshot.FirewallStatus}.";
            }

            if (ContainsAny(normalized, "memory", "ram"))
            {
                string contributors = string.IsNullOrWhiteSpace(snapshot.MemoryTopContributors)
                    ? string.Empty
                    : $" Largest application contributors: {snapshot.MemoryTopContributors}.";

                return $"Memory use is {snapshot.MemoryUsagePercent:0.0}% ({snapshot.MemoryUsedGB:0.00} GB of {snapshot.MemoryTotalGB:0.00} GB). " +
                       $"Sentinel classifies current memory pressure as {snapshot.MemoryPressureLevel}.{contributors}";
            }

            if (ContainsAny(normalized, "cpu", "processor"))
            {
                return $"Current verified CPU usage is {snapshot.CpuUsagePercent:0.0}%.";
            }

            if (ContainsAny(normalized, "disk", "storage", "drive space"))
            {
                return snapshot.DiskTotalGB > 0
                    ? $"Current verified disk usage is {snapshot.DiskUsagePercent:0.0}%, with {snapshot.DiskFreeGB:0.00} GB free of {snapshot.DiskTotalGB:0.00} GB."
                    : "Sentinel does not currently have verified disk-capacity evidence for this question.";
            }

            if (ContainsAny(normalized, "network", "internet", "connection", "download", "upload"))
            {
                if (!snapshot.NetworkConnectionMonitoringAvailable)
                {
                    return $"Network connection monitoring is currently {snapshot.NetworkConnectionMonitoringStatus}. Sentinel cannot verify active network health from the current evidence.";
                }

                string flagged = snapshot.FlaggedConnectionCount > 0
                    ? $" Sentinel has flagged {snapshot.FlaggedConnectionCount} connection condition(s); the primary finding is {snapshot.PrimaryFlaggedConnectionProcessName} to {snapshot.PrimaryFlaggedConnectionRemoteEndpoint}: {snapshot.PrimaryFlaggedConnectionReason}"
                    : " Sentinel has not flagged an active TCP connection condition.";

                return $"Network monitoring is active. Sentinel sees {snapshot.EstablishedConnectionCount} established TCP connection(s), including {snapshot.ExternalConnectionCount} external connection(s). Current measured throughput is {snapshot.DownloadMbps:0.00} Mbps down and {snapshot.UploadMbps:0.00} Mbps up.{flagged}";
            }

            if (ContainsAny(normalized, "startup app", "startup apps", "starts with windows", "startup program", "startup entry"))
            {
                return snapshot.FlaggedStartupEntryCount > 0
                    ? $"Sentinel verified {snapshot.StartupEntryCount} startup entries and flagged {snapshot.FlaggedStartupEntryCount} for review. Primary finding: {snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}"
                    : $"Sentinel verified {snapshot.StartupEntryCount} startup entries and found no unusual startup persistence entry.";
            }

            if (ContainsAny(normalized, "running service", "running services", "windows service", "services"))
            {
                return snapshot.FlaggedServiceCount > 0
                    ? $"Sentinel verified {snapshot.RunningServiceCount} running services out of {snapshot.InstalledServiceCount} installed services and flagged {snapshot.FlaggedServiceCount} for review. Primary finding: {snapshot.PrimaryFlaggedServiceName}: {snapshot.PrimaryFlaggedServiceReason}"
                    : $"Sentinel verified {snapshot.RunningServiceCount} running services out of {snapshot.InstalledServiceCount} installed services and found no service warning condition.";
            }

            if (ContainsAny(normalized, "top process", "top processes", "highest memory", "most memory", "running process", "running processes"))
            {
                return snapshot.HighestMemoryProcessGB > 0
                    ? $"Sentinel currently sees {snapshot.ProcessCount} running processes. The highest-memory process is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB."
                    : $"Sentinel currently sees {snapshot.ProcessCount} running processes, but does not yet have a verified top-memory process result.";
            }

            if (ContainsAny(normalized, "defender", "antivirus", "virus protection"))
            {
                return $"Microsoft Defender status is currently reported as {snapshot.DefenderStatus}.";
            }

            if (ContainsAny(normalized, "firewall"))
            {
                return $"Windows Firewall status is currently reported as {snapshot.FirewallStatus}.";
            }

            if (ContainsAny(normalized, "security", "secure", "threat", "malware", "virus"))
            {
                if (snapshot.FlaggedProcessCount > 0)
                {
                    return $"Sentinel has flagged {snapshot.FlaggedProcessCount} process condition(s). " +
                           $"The primary verified process finding is {snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}";
                }

                if (!snapshot.DefenderEnabled || !snapshot.FirewallEnabled)
                {
                    return $"Sentinel's verified security posture shows Defender {snapshot.DefenderStatus} and Firewall {snapshot.FirewallStatus}.";
                }

                return "Sentinel's current verified evidence does not show a flagged process condition, and the monitored Defender and Firewall protections are enabled. This does not prove that no threat exists.";
            }

            if (ContainsAny(normalized, "process", "app", "application", "program"))
            {
                if (snapshot.FlaggedProcessCount > 0)
                {
                    return $"Sentinel has flagged {snapshot.FlaggedProcessCount} process condition(s). " +
                           $"Primary finding: {snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}";
                }

                return snapshot.HighestMemoryProcessGB > 0
                    ? $"Sentinel currently sees {snapshot.ProcessCount} running processes. The highest-memory process is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB."
                    : $"Sentinel currently sees {snapshot.ProcessCount} running processes and has no verified flagged-process condition.";
            }

            if (ContainsAny(normalized, "what happened", "why", "cause", "caused", "investigation"))
            {
                if (!snapshot.InvestigationRequiresAttention)
                {
                    return "Sentinel does not currently have an active verified investigation finding that requires your attention.";
                }

                return Safe(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened);
            }

            if (ContainsAny(normalized, "recommend", "should i", "what should", "fix", "do about"))
            {
                return snapshot.InvestigationRequiresAttention
                    ? Safe(snapshot.GuidanceRecommendedAction, snapshot.Recommendation)
                    : "No action is currently required based on Sentinel's verified evidence. Sentinel will continue monitoring your computer.";
            }

            return InsufficientEvidence;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            foreach (string term in terms)
            {
                if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Safe(string primary, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(primary) && !primary.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return primary.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fallback) && !fallback.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return fallback.Trim();
            }

            return InsufficientEvidence;
        }
    }
}
