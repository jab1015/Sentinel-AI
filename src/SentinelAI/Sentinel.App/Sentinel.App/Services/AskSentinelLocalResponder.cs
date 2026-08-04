/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class AskSentinelLocalResponder
    {
        private const string InsufficientEvidence = "Sentinel does not yet have enough verified information to answer that question.";
        private readonly WindowsHealthEvidenceProvider _windowsHealth = new();

        public string Answer(string question, SystemSnapshot snapshot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);
            string q = question.Trim().ToLowerInvariant();

            if (Has(q, "verify local health", "run local health verification", "verify ask sentinel local"))
            {
                return BuildLocalHealthVerification(snapshot);
            }

            if (IsWindowsUpdateQuestion(q)) return _windowsHealth.GetWindowsUpdateStatus();
            if (Has(q, "pending restart", "restart required", "reboot required", "need to restart")) return _windowsHealth.GetPendingRestartStatus();
            if (Has(q, "tpm", "trusted platform module")) return _windowsHealth.GetTpmStatus();
            if (Has(q, "secure boot")) return _windowsHealth.GetSecureBootStatus();
            if (Has(q, "bitlocker", "device encryption", "drive encryption")) return _windowsHealth.GetBitLockerStatus();

            if (Has(q, "healthy", "health", "overall status", "anything wrong", "problem", "attention"))
                return snapshot.InvestigationRequiresAttention
                    ? $"Sentinel currently has a verified condition that requires attention. {Safe(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened)}"
                    : $"Sentinel's verified evidence shows no condition requiring attention. CPU is {snapshot.CpuUsagePercent:0.0}%, memory is {snapshot.MemoryUsagePercent:0.0}%, disk use is {snapshot.DiskUsagePercent:0.0}%, Defender is {snapshot.DefenderStatus}, and Firewall is {snapshot.FirewallStatus}.";

            if (Has(q, "memory", "ram"))
                return $"Memory use is {snapshot.MemoryUsagePercent:0.0}% ({snapshot.MemoryUsedGB:0.00} GB of {snapshot.MemoryTotalGB:0.00} GB). Pressure is {snapshot.MemoryPressureLevel}. Largest contributors: {snapshot.MemoryTopContributors}";

            if (Has(q, "cpu", "processor")) return $"Current verified CPU usage is {snapshot.CpuUsagePercent:0.0}%.";

            if (Has(q, "disk", "storage", "drive space"))
                return snapshot.DiskTotalGB > 0
                    ? $"Current verified disk usage is {snapshot.DiskUsagePercent:0.0}%, with {snapshot.DiskFreeGB:0.00} GB free of {snapshot.DiskTotalGB:0.00} GB."
                    : "Sentinel does not currently have verified disk-capacity evidence.";

            if (Has(q, "network", "internet", "connection", "download", "upload"))
                return snapshot.NetworkConnectionMonitoringAvailable
                    ? $"Network monitoring is active. Sentinel sees {snapshot.EstablishedConnectionCount} established TCP connections, {snapshot.ExternalConnectionCount} external connections, and {snapshot.FlaggedConnectionCount} flagged conditions. Throughput is {snapshot.DownloadMbps:0.00} Mbps down and {snapshot.UploadMbps:0.00} Mbps up."
                    : $"Network connection monitoring is {snapshot.NetworkConnectionMonitoringStatus}; active network health cannot currently be verified.";

            if (Has(q, "startup app", "startup apps", "starts with windows", "startup program", "startup entry"))
                return snapshot.FlaggedStartupEntryCount > 0
                    ? $"Sentinel verified {snapshot.StartupEntryCount} startup entries and flagged {snapshot.FlaggedStartupEntryCount}. Primary finding: {snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}"
                    : $"Sentinel verified {snapshot.StartupEntryCount} startup entries and found no unusual startup persistence entry.";

            if (Has(q, "running service", "running services", "windows service", "services"))
                return snapshot.FlaggedServiceCount > 0
                    ? $"Sentinel verified {snapshot.RunningServiceCount} running services out of {snapshot.InstalledServiceCount} installed and flagged {snapshot.FlaggedServiceCount}. Primary finding: {snapshot.PrimaryFlaggedServiceName}: {snapshot.PrimaryFlaggedServiceReason}"
                    : $"Sentinel verified {snapshot.RunningServiceCount} running services out of {snapshot.InstalledServiceCount} installed and found no service warning condition.";

            if (Has(q, "top process", "top processes", "highest memory", "most memory", "running process", "running processes"))
                return snapshot.HighestMemoryProcessGB > 0
                    ? $"Sentinel sees {snapshot.ProcessCount} running processes. The highest-memory process is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB."
                    : $"Sentinel sees {snapshot.ProcessCount} running processes but does not yet have a verified top-memory result.";

            if (Has(q, "defender", "antivirus", "virus protection")) return $"Microsoft Defender status is {snapshot.DefenderStatus}.";
            if (Has(q, "firewall")) return $"Windows Firewall status is {snapshot.FirewallStatus}.";

            if (Has(q, "security", "secure", "threat", "malware", "virus"))
            {
                if (snapshot.FlaggedProcessCount > 0) return $"Sentinel flagged {snapshot.FlaggedProcessCount} process conditions. Primary finding: {snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}";
                if (!snapshot.DefenderEnabled || !snapshot.FirewallEnabled) return $"Verified security posture: Defender {snapshot.DefenderStatus}; Firewall {snapshot.FirewallStatus}.";
                return "Current verified evidence shows no flagged process condition, and Defender and Firewall are enabled. This does not prove that no threat exists.";
            }

            if (Has(q, "process", "app", "application", "program"))
                return snapshot.FlaggedProcessCount > 0
                    ? $"Sentinel flagged {snapshot.FlaggedProcessCount} process conditions. Primary finding: {snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}"
                    : $"Sentinel sees {snapshot.ProcessCount} running processes. Highest memory: {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB.";

            if (Has(q, "what happened", "why", "cause", "caused", "investigation"))
                return snapshot.InvestigationRequiresAttention ? Safe(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened) : "Sentinel has no active verified investigation finding requiring attention.";

            if (Has(q, "recommend", "should i", "what should", "fix", "do about"))
                return snapshot.InvestigationRequiresAttention ? Safe(snapshot.GuidanceRecommendedAction, snapshot.Recommendation) : "No action is required based on current verified evidence. Sentinel will continue monitoring.";

            return InsufficientEvidence;
        }

        private string BuildLocalHealthVerification(SystemSnapshot snapshot)
        {
            string update = _windowsHealth.GetWindowsUpdateStatus();
            string restart = _windowsHealth.GetPendingRestartStatus();
            string tpm = _windowsHealth.GetTpmStatus();
            string secureBoot = _windowsHealth.GetSecureBootStatus();
            string bitLocker = _windowsHealth.GetBitLockerStatus();

            return "Ask Sentinel Local verification completed for 14 evidence areas. " +
                   $"Windows Update: {update} Pending restart: {restart} TPM: {tpm} Secure Boot: {secureBoot} BitLocker: {bitLocker} " +
                   $"Defender: {snapshot.DefenderStatus}. Firewall: {snapshot.FirewallStatus}. CPU: {snapshot.CpuUsagePercent:0.0}%. " +
                   $"Memory: {snapshot.MemoryUsagePercent:0.0}% ({snapshot.MemoryUsedGB:0.00} GB of {snapshot.MemoryTotalGB:0.00} GB). " +
                   $"Disk: {snapshot.DiskUsagePercent:0.0}% used, {snapshot.DiskFreeGB:0.00} GB free. " +
                   $"Network: {snapshot.NetworkConnectionMonitoringStatus}, {snapshot.EstablishedConnectionCount} established connections. " +
                   $"Startup apps: {snapshot.StartupEntryCount} entries, {snapshot.FlaggedStartupEntryCount} flagged. " +
                   $"Running services: {snapshot.RunningServiceCount} of {snapshot.InstalledServiceCount}. " +
                   $"Top processes: {snapshot.ProcessCount} running; highest memory is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB.";
        }

        private static bool IsWindowsUpdateQuestion(string value)
        {
            return Has(
                value,
                "windows update",
                "windows updates",
                "update status",
                "check for updates",
                "latest update",
                "latest updates",
                "up to date",
                "fully updated",
                "updates installed",
                "missing updates",
                "available updates");
        }

        private static bool Has(string value, params string[] terms)
        {
            foreach (string term in terms) if (value.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Safe(string primary, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(primary) && !primary.Equals("None", StringComparison.OrdinalIgnoreCase)) return primary.Trim();
            if (!string.IsNullOrWhiteSpace(fallback) && !fallback.Equals("None", StringComparison.OrdinalIgnoreCase)) return fallback.Trim();
            return InsufficientEvidence;
        }
    }
}
