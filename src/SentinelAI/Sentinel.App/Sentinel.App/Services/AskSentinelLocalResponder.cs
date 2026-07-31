/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Provides immediate, strictly evidence-grounded answers for the initial Ask Sentinel
    /// interaction surface. Unsupported questions fail closed instead of inferring facts.
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

            if (ContainsAny(normalized, "healthy", "health", "status", "anything wrong", "problem", "attention"))
            {
                return snapshot.InvestigationRequiresAttention
                    ? $"Sentinel currently has a verified condition that requires attention. {Safe(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened)}"
                    : "Sentinel's current verified evidence does not show a condition that requires your attention.";
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

            if (ContainsAny(normalized, "disk", "storage", "drive"))
            {
                return snapshot.DiskTotalGB > 0
                    ? $"Current verified disk usage is {snapshot.DiskUsagePercent:0.0}%, with {snapshot.DiskFreeGB:0.00} GB free of {snapshot.DiskTotalGB:0.00} GB."
                    : "Sentinel does not currently have verified disk-capacity evidence for this question.";
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
