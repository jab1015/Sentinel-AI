/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class AskSentinelLocalResponder
    {
        private const string InsufficientEvidence = "Sentinel does not yet have enough verified information to answer that question.";
        private readonly WindowsHealthEvidenceProvider _windowsHealth = new();
        private readonly DriverHealthEvidenceProvider _driverHealth = new();
        private readonly PersistentInvestigationMemoryService _persistentMemory = new();

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
            if (IsPendingRestartQuestion(q)) return _windowsHealth.GetPendingRestartStatus();
            if (IsTpmQuestion(q)) return _windowsHealth.GetTpmStatus();
            if (IsSecureBootQuestion(q)) return _windowsHealth.GetSecureBootStatus();
            if (IsBitLockerQuestion(q)) return _windowsHealth.GetBitLockerStatus();
            // Crash intent must win over an active driver finding. A driver is not
            // the crash cause unless crash-specific evidence establishes that link.
            if (IsCrashQuestion(q)) return BuildCrashAnswer(snapshot, q);
            if (IsDriverHealthQuestion(q))
            {
                string? persistentAnswer = BuildPersistentDriverAnswer(snapshot);
                return persistentAnswer ?? _driverHealth.GetDriverHealthStatus();
            }

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
                return !snapshot.StartupPersistenceMonitoringAvailable
                    ? "Sentinel could not completely collect current startup-persistence evidence, so I cannot verify that startup entries are clean."
                    : snapshot.FlaggedStartupEntryCount > 0
                        ? $"Sentinel verified {snapshot.StartupEntryCount} startup entries and flagged {snapshot.FlaggedStartupEntryCount}. Primary finding: {snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}"
                        : $"Sentinel verified {snapshot.StartupEntryCount} startup entries and found no unusual startup persistence entry.";

            if (Has(q, "scheduled task", "scheduled tasks", "task scheduler", "scheduled persistence"))
                return !snapshot.ScheduledTaskMonitoringAvailable
                    ? "Sentinel could not collect current scheduled-task evidence, so I cannot verify that scheduled tasks are clean."
                    : snapshot.FlaggedScheduledTaskCount > 0
                        ? $"Sentinel verified {snapshot.ScheduledTaskCount} scheduled tasks and flagged {snapshot.FlaggedScheduledTaskCount}. Primary finding: {snapshot.PrimaryFlaggedScheduledTaskName}: {snapshot.PrimaryFlaggedScheduledTaskReason}"
                        : $"Sentinel verified {snapshot.ScheduledTaskCount} scheduled tasks and found no unusual scheduled-task persistence.";

            if (Has(q, "running service", "running services", "windows service", "services"))
                return !snapshot.ServiceMonitoringAvailable
                    ? "Sentinel could not completely collect current Windows service and persistence evidence, so I cannot verify that services are clean."
                    : snapshot.FlaggedServiceCount > 0
                        ? $"Sentinel verified {snapshot.RunningServiceCount} running services out of {snapshot.InstalledServiceCount} installed and flagged {snapshot.FlaggedServiceCount}. Primary finding: {snapshot.PrimaryFlaggedServiceName}: {snapshot.PrimaryFlaggedServiceReason}"
                        : $"Sentinel verified {snapshot.RunningServiceCount} running services out of {snapshot.InstalledServiceCount} installed and found no service warning condition.";

            if (Has(q, "top process", "top processes", "highest memory", "most memory", "running process", "running processes"))
                return !snapshot.ProcessMonitoringAvailable
                    ? "Sentinel could not collect current process evidence, so I cannot verify running-process or top-memory results."
                    : snapshot.HighestMemoryProcessGB > 0
                        ? $"Sentinel sees {snapshot.ProcessCount} running processes. The highest-memory process is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB."
                        : $"Sentinel sees {snapshot.ProcessCount} running processes but does not yet have a verified top-memory result.";

            if (Has(q, "defender", "antivirus", "virus protection")) return $"Microsoft Defender status is {snapshot.DefenderStatus}.";
            if (Has(q, "firewall")) return $"Windows Firewall status is {snapshot.FirewallStatus}.";

            if (Has(q, "security", "secure", "threat", "malware", "virus"))
            {
                if (snapshot.SpywareCorrelationState.Equals("HighConcern", StringComparison.OrdinalIgnoreCase) ||
                    snapshot.SpywareCorrelationState.Equals("Review", StringComparison.OrdinalIgnoreCase))
                    return snapshot.SpywareCorrelationSummary;

                if (!snapshot.ProtectionHealthFullyProtected)
                    return snapshot.ProtectionHealthSummary;

                if (snapshot.FlaggedProcessCount > 0)
                    return $"Sentinel flagged {snapshot.FlaggedProcessCount} process conditions. Primary finding: {snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}";

                return "Current verified evidence shows no corroborated security condition, monitoring coverage is active, and Defender and Firewall are enabled. This does not prove that no threat exists.";
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

        private string? BuildPersistentDriverAnswer(SystemSnapshot snapshot)
        {
            try
            {
                var records = _persistentMemory.ReadAllAsync().GetAwaiter().GetResult();
                PersistentInvestigationRecord? record = records
                    .Where(item => item.FindingType.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                    .Where(item => item.State == InvestigationLifecycleState.PersistentNoncritical)
                    .Where(item => MatchesCurrentDriver(snapshot, item))
                    .OrderByDescending(item => item.LastVerifiedUtc)
                    .FirstOrDefault();

                if (record is null) return null;

                string notificationState = record.NotificationsSuppressed
                    ? "I am monitoring this exact condition silently."
                    : "Notifications are still enabled for this condition. You can choose Monitor Silently on the dashboard.";

                return
                    "Driver health\n\n" +
                    "Known persistent condition.\n" +
                    $"What I found\n{record.RootCause}\n\n" +
                    "What I verified\n" +
                    "I completed the available safe driver investigation and found no remaining verified safe repair path for this exact condition.\n\n" +
                    $"Investigation result\n{record.EvidenceSummary}\n\n" +
                    "What happens next\n" +
                    $"{notificationState} Monitoring continues either way, and I will reopen the investigation automatically if material evidence changes.\n\n" +
                    $"Confidence: {record.ConfidencePercent}%. Trust: {record.TrustLevel}.";
            }
            catch
            {
                return null;
            }
        }

        private static bool MatchesCurrentDriver(SystemSnapshot snapshot, PersistentInvestigationRecord record)
        {
            string rootCause = record.RootCause?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootCause)) return false;

            return Contains(snapshot.GuidanceWhatHappened, rootCause) ||
                   Contains(snapshot.InvestigationSummary, rootCause) ||
                   Contains(snapshot.GuidanceEvidence, rootCause) ||
                   Contains(rootCause, "Intel(R) Management Engine Interface") &&
                   (Contains(snapshot.GuidanceWhatHappened, "Management Engine Interface") ||
                    Contains(snapshot.InvestigationSummary, "Management Engine Interface") ||
                    Contains(snapshot.GuidanceEvidence, "Management Engine Interface"));
        }

        private string BuildLocalHealthVerification(SystemSnapshot snapshot)
        {
            string update = _windowsHealth.GetWindowsUpdateStatus();
            string restart = _windowsHealth.GetPendingRestartStatus();
            string tpm = _windowsHealth.GetTpmStatus();
            string secureBoot = _windowsHealth.GetSecureBootStatus();
            string bitLocker = _windowsHealth.GetBitLockerStatus();

            return "Ask Sentinel Local reported the current available evidence across its local health areas. " +
                   $"Windows Update: {update} Pending restart: {restart} TPM: {tpm} Secure Boot: {secureBoot} BitLocker: {bitLocker} " +
                   $"Defender: {snapshot.DefenderStatus}. Firewall: {snapshot.FirewallStatus}. CPU: {snapshot.CpuUsagePercent:0.0}%. " +
                   $"Memory: {snapshot.MemoryUsagePercent:0.0}% ({snapshot.MemoryUsedGB:0.00} GB of {snapshot.MemoryTotalGB:0.00} GB). " +
                   $"Disk: {snapshot.DiskUsagePercent:0.0}% used, {snapshot.DiskFreeGB:0.00} GB free. " +
                   $"Network: {snapshot.NetworkConnectionMonitoringStatus}, {snapshot.EstablishedConnectionCount} established connections. " +
                   $"Startup apps: {snapshot.StartupEntryCount} entries, {snapshot.FlaggedStartupEntryCount} flagged. " +
                   $"Running services: {snapshot.RunningServiceCount} of {snapshot.InstalledServiceCount}. " +
                   $"Top processes: {snapshot.ProcessCount} running; highest memory is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB.";
        }

        private static bool IsWindowsUpdateQuestion(string value) =>
            Has(value, "windows update", "windows updates", "update status", "check for updates", "latest update", "latest updates", "up to date", "fully updated", "updates installed", "missing updates", "available updates", "need updates", "need an update", "current on updates");

        private static bool IsPendingRestartQuestion(string value) =>
            Has(value, "pending restart", "restart pending", "restart required", "reboot required", "need to restart", "need a restart", "need to reboot", "should i restart", "should i reboot");

        private static bool IsTpmQuestion(string value) =>
            Has(value, "tpm", "trusted platform module", "security processor", "hardware security module");

        private static bool IsSecureBootQuestion(string value) =>
            Has(value, "secure boot", "secureboot", "uefi security", "boot security");

        private static bool IsBitLockerQuestion(string value) =>
            Has(value, "bitlocker", "bit locker", "device encryption", "drive encryption", "disk encryption", "encrypted drive", "is my drive encrypted");

        private static bool IsDriverHealthQuestion(string value) =>
            Has(value,
                "driver conflict",
                "driver conflicts",
                "driver problem",
                "driver problems",
                "driver failure",
                "driver failures",
                "driver error",
                "driver errors",
                "device manager issue",
                "device manager problem",
                "problem device",
                "problem devices",
                "unsigned driver",
                "unsigned drivers",
                "driver signature",
                "driver signatures",
                "drivers healthy",
                "are my drivers healthy");

        private static bool IsCrashQuestion(string value) =>
            Has(value, "blue screen", "blue-screen", "blue screened", "blue-screened",
                "bluescreen", "bsod", "bsd", "bds", "bugcheck", "bug check",
                "stop code", "stop error", "crashed", "system crash", "computer crash",
                "unexpected restart");

        private static string BuildCrashAnswer(SystemSnapshot snapshot, string question)
        {
            bool asksAboutSlowness = Has(question, "slow", "sluggish", "lag", "lagging", "performance", "freeze", "freezing");
            string performance = asksAboutSlowness ? BuildPostCrashPerformanceAnswer(snapshot) : string.Empty;

            if (!snapshot.CrashEvidenceAvailable)
                return "Sentinel could not access Windows crash evidence during this check, so I cannot verify why the computer stopped. I will not treat an unrelated active finding as the crash cause." + performance;

            string timing = snapshot.RecentCrashTime.HasValue
                ? $" Windows recorded the event at {snapshot.RecentCrashTime.Value:MMM d, yyyy h:mm tt}."
                : string.Empty;

            if (!snapshot.RecentCrashDetected)
                return "Sentinel did not find a Windows crash event or recent minidump in the last 7 days, so I cannot verify what caused the reported stop." + performance;

            return snapshot.RecentCrashSummary + timing +
                " Sentinel will not name a driver, application, or hardware component as the cause unless crash-specific evidence supports that connection." +
                performance;
        }

        private static string BuildPostCrashPerformanceAnswer(SystemSnapshot snapshot)
        {
            string measurements =
                $" Current performance evidence is separate from the crash cause: CPU {snapshot.CpuUsagePercent:0.0}%, memory {snapshot.MemoryUsagePercent:0.0}%, and disk use {snapshot.DiskUsagePercent:0.0}%.";

            if (snapshot.MemoryUsagePercent >= 90)
                return measurements + $" Memory pressure is currently very high; {snapshot.HighestMemoryProcessName} is the largest measured contributor. This can explain current slowness, but it does not establish why Windows crashed.";

            if (snapshot.CpuUsagePercent >= 85)
                return measurements + " CPU use is currently very high and can explain current slowness, but it does not establish why Windows crashed.";

            if (snapshot.DiskUsagePercent >= 95)
                return measurements + " The system drive is critically full and can reduce current performance and reliability, but it does not establish why Windows crashed.";

            return measurements + " These current readings do not show severe resource saturation. A brief post-restart slowdown may have ended before this snapshot, so Sentinel will continue monitoring rather than inventing a cause.";
        }

        private static bool Has(string value, params string[] terms)
        {
            foreach (string term in terms) if (value.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool Contains(string? value, string term) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

        private static string Safe(string primary, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(primary) && !primary.Equals("None", StringComparison.OrdinalIgnoreCase)) return primary.Trim();
            if (!string.IsNullOrWhiteSpace(fallback) && !fallback.Equals("None", StringComparison.OrdinalIgnoreCase)) return fallback.Trim();
            return InsufficientEvidence;
        }
    }
}

