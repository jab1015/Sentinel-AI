/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
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
        private readonly PerformanceBaselineService _performanceBaseline = new();
        private readonly StartupLogonEvidenceProvider _startupLogon = new();

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
            if (IsStartupLogonPerformanceQuestion(q)) return _startupLogon.GetStartupLogonEvidence(snapshot);
            if (IsPerformanceQuestion(q)) return BuildPerformanceAnswer(snapshot);
            if (IsBroadComputerOverviewQuestion(q)) return BuildComputerOverviewAnswer(snapshot);

            string? remediationIntent = BuildRemediationIntentAnswer(q, snapshot);
            if (!string.IsNullOrWhiteSpace(remediationIntent)) return remediationIntent;

            string? detailedFinding = BuildDetailedFindingAnswer(q, snapshot);
            if (!string.IsNullOrWhiteSpace(detailedFinding)) return detailedFinding;

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
            {
                if (!snapshot.NetworkConnectionMonitoringAvailable)
                    return $"Network connection monitoring is {snapshot.NetworkConnectionMonitoringStatus}; active network health cannot currently be verified.";

                bool asksForFindingDetail = Has(q,
                    "which", "what connection", "what network", "flagged", "why", "reason",
                    "details", "show me", "which connection", "which network");

                if (asksForFindingDetail && snapshot.FlaggedConnectionCount > 0)
                {
                    string process = string.IsNullOrWhiteSpace(snapshot.PrimaryFlaggedConnectionProcessName) ||
                                     snapshot.PrimaryFlaggedConnectionProcessName.Equals("None", StringComparison.OrdinalIgnoreCase)
                        ? "Unknown process"
                        : snapshot.PrimaryFlaggedConnectionProcessName;
                    string endpoint = string.IsNullOrWhiteSpace(snapshot.PrimaryFlaggedConnectionRemoteEndpoint) ||
                                      snapshot.PrimaryFlaggedConnectionRemoteEndpoint.Equals("None", StringComparison.OrdinalIgnoreCase)
                        ? "remote endpoint unavailable"
                        : snapshot.PrimaryFlaggedConnectionRemoteEndpoint;
                    string reason = string.IsNullOrWhiteSpace(snapshot.PrimaryFlaggedConnectionReason)
                        ? "Sentinel recorded a network review condition but did not capture a specific reason."
                        : snapshot.PrimaryFlaggedConnectionReason;

                    string correlation = !string.IsNullOrWhiteSpace(snapshot.ConnectionIntelligenceSummary) &&
                                         !snapshot.ConnectionIntelligenceSummary.Equals("Sentinel is correlating current network activity with local system evidence.", StringComparison.OrdinalIgnoreCase)
                        ? $"\n\nCorrelation: {snapshot.ConnectionIntelligenceSummary}"
                        : string.Empty;

                    return
                        $"Sentinel has {snapshot.FlaggedConnectionCount} flagged network condition{(snapshot.FlaggedConnectionCount == 1 ? string.Empty : "s")}. " +
                        $"The primary flagged connection is {process} -> {endpoint}.\n\n" +
                        $"Why it was flagged: {reason}{correlation}\n\n" +
                        $"Current network context: {snapshot.EstablishedConnectionCount} established TCP connections, " +
                        $"{snapshot.ExternalConnectionCount} external connections ({snapshot.OutboundExternalConnectionCount} outbound, " +
                        $"{snapshot.InboundExternalConnectionCount} inbound), with {snapshot.FlaggedConnectionCount} flagged.";
                }

                return $"Network monitoring is active. Sentinel sees {snapshot.EstablishedConnectionCount} established TCP connections, {snapshot.ExternalConnectionCount} external connections, and {snapshot.FlaggedConnectionCount} flagged conditions. Throughput is {snapshot.DownloadMbps:0.00} Mbps down and {snapshot.UploadMbps:0.00} Mbps up.";
            }

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
            {
                bool asksForFindingDetail = Has(q, "which", "what", "flagged", "why", "reason", "details", "show me");
                if (snapshot.FlaggedProcessCount > 0 && asksForFindingDetail)
                    return $"Sentinel flagged {snapshot.FlaggedProcessCount} process condition{(snapshot.FlaggedProcessCount == 1 ? string.Empty : "s")}. Primary flagged process: {snapshot.PrimaryFlaggedProcessName} (PID {snapshot.PrimaryFlaggedProcessId}). Why it was flagged: {snapshot.PrimaryFlaggedProcessReason}";

                return snapshot.FlaggedProcessCount > 0
                    ? $"Sentinel flagged {snapshot.FlaggedProcessCount} process conditions. Primary finding: {snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}"
                    : $"Sentinel sees {snapshot.ProcessCount} running processes. Highest memory: {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB.";
            }

            if (Has(q, "what happened", "why", "cause", "caused", "investigation"))
                return snapshot.InvestigationRequiresAttention ? Safe(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened) : "Sentinel has no active verified investigation finding requiring attention.";

            if (Has(q, "recommend", "should i", "what should", "fix", "do about"))
                return snapshot.InvestigationRequiresAttention ? Safe(snapshot.GuidanceRecommendedAction, snapshot.Recommendation) : "No action is required based on current verified evidence. Sentinel will continue monitoring.";

            return InsufficientEvidence;
        }

        private static string? BuildRemediationIntentAnswer(string question, SystemSnapshot snapshot)
        {
            bool actionIntent = Has(question,
                "quarantine", "contain", "block", "unblock", "restore", "undo", "rollback",
                "reverse", "revert", "repair", "fix", "restart", "remove the block",
                "mess something up", "break something", "causes a problem", "if it breaks");
            if (!actionIntent) return null;

            bool asksAboutNetwork = Has(question, "network", "connection", "traffic", "endpoint", "firewall");
            bool asksAboutFile = Has(question, "file", "folder");
            bool asksAboutProcess = Has(question, "process", "program", "application", "app");
            bool asksAboutService = Has(question, "service", "windows service");

            if (asksAboutNetwork || (!asksAboutFile && !asksAboutProcess && !asksAboutService &&
                                    Has(question, "quarantine", "block", "unblock", "restore")))
            {
                string endpoint = Friendly(snapshot.PrimaryFlaggedConnectionRemoteEndpoint, "the exact remote endpoint");
                bool hasCurrentNetworkAction =
                    snapshot.AutonomousProtectionRequiresUserApproval &&
                    snapshot.AutonomousProtectionAction.Equals("block-outbound-endpoint", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionTarget) &&
                    !snapshot.AutonomousProtectionTarget.Equals("None", StringComparison.OrdinalIgnoreCase);

                string availability = hasCurrentNetworkAction
                    ? $"A current approval-gated network containment action is available for {snapshot.AutonomousProtectionTarget}. Sentinel must get your approval before applying it."
                    : BuildNetworkContainmentUnavailableReason(snapshot);

                return
                    "Network containment and rollback\n\n" +
                    $"For a suspicious connection, Sentinel does not move the connection into a file-style quarantine. It contains the destination by creating an exact outbound Windows Firewall block for {endpoint}. " +
                    "Sentinel verifies the rule after creation and checks connectivity before and after the change. If general connectivity is lost immediately after containment, Sentinel automatically removes the new rule and verifies that rollback.\n\n" +
                    "If the block later causes a problem for a specific application while the rest of the internet still works, automatic rollback may not trigger. In that case Sentinel can remove the exact Sentinel-created firewall block and verify that it is gone.\n\n" +
                    availability;
            }

            if (asksAboutFile)
            {
                return
                    "File quarantine and restore\n\n" +
                    "Sentinel's file quarantine is reversible. A quarantined file is moved into Sentinel's protected quarantine store with verified metadata. If you later choose Restore, Sentinel uses the protected quarantine record to restore that exact file and verifies the result. Permanent delete is a separate action and cannot be undone.";
            }

            if (asksAboutProcess)
            {
                return
                    "Process containment\n\n" +
                    "Sentinel can contain an exact verified process instance when a supported approval-gated action is available. Process containment is not a reversible quarantine: if a process is terminated, Sentinel cannot restore that same running process instance. The application may be relaunched later if it is safe to do so.";
            }

            if (asksAboutService)
            {
                return
                    "Service remediation\n\n" +
                    "A service restart is an approval-gated repair action, not a quarantine. Sentinel verifies the exact service before acting and verifies that it is running afterward. There is no separate 'restore' object for a restart; if a configuration change were ever required, that would need its own verified remediation and rollback path.";
            }

            if (snapshot.AutonomousProtectionRequiresUserApproval &&
                !string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionAction) &&
                !snapshot.AutonomousProtectionAction.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return
                    $"Sentinel currently has an approval-gated action available: {snapshot.AutonomousProtectionAction} targeting {snapshot.AutonomousProtectionTarget}. " +
                    "Sentinel should explain the exact effect and rollback behavior for that action before asking you to approve it.";
            }

            return null;
        }

        private static string BuildNetworkContainmentUnavailableReason(SystemSnapshot snapshot)
        {
            if (!snapshot.NetworkConnectionMonitoringAvailable)
                return $"Sentinel cannot offer containment because network monitoring is currently {snapshot.NetworkConnectionMonitoringStatus}.";

            if (snapshot.FlaggedConnectionCount <= 0)
                return "Sentinel cannot offer containment because the refreshed evidence no longer contains a flagged network destination.";

            if (!snapshot.ConnectionIntelligenceHasCorroboratingEvidence)
                return $"Sentinel is withholding containment because this connection is still an uncorroborated network finding (confidence {snapshot.ConnectionIntelligenceConfidenceScore}%). The current policy requires corroborating evidence before Sentinel can prepare a firewall-changing action.";

            if (snapshot.ConnectionIntelligenceConfidenceScore < 80)
                return $"Sentinel is withholding containment because the corroborated network evidence is only {snapshot.ConnectionIntelligenceConfidenceScore}% confidence. The current high-confidence containment threshold is 80%.";

            return $"Sentinel is withholding containment because the current investigation state '{snapshot.InvestigationReasonCode}' did not produce a supported exact-target network action. No firewall change will be made unless that verified remediation state is present.";
        }

        private static string? BuildDetailedFindingAnswer(string question, SystemSnapshot snapshot)
        {
            bool asksForDetail = Has(question,
                "which", "what ", "what's", "whats", "flagged", "why", "reason",
                "details", "detail", "show me", "identify", "name the");
            if (!asksForDetail) return null;

            if (Has(question, "network", "connection", "internet", "traffic"))
            {
                if (!snapshot.NetworkConnectionMonitoringAvailable) return null;
                if (snapshot.FlaggedConnectionCount <= 0)
                    return "Sentinel does not currently have a flagged network connection to identify.";

                string process = Friendly(snapshot.PrimaryFlaggedConnectionProcessName, "Unknown process");
                string endpoint = Friendly(snapshot.PrimaryFlaggedConnectionRemoteEndpoint, "remote endpoint unavailable");
                string reason = Friendly(snapshot.PrimaryFlaggedConnectionReason, "No specific network flag reason was recorded.");
                string correlation = Friendly(snapshot.ConnectionIntelligenceSummary, string.Empty);

                return
                    $"Flagged network finding\n\n" +
                    $"Process: {process}\n" +
                    $"Remote endpoint: {endpoint}\n" +
                    $"Reason: {reason}" +
                    (string.IsNullOrWhiteSpace(correlation) ? string.Empty : $"\nCorrelation: {correlation}") +
                    $"\n\nSentinel currently sees {snapshot.FlaggedConnectionCount} flagged network condition{(snapshot.FlaggedConnectionCount == 1 ? string.Empty : "s")} out of {snapshot.EstablishedConnectionCount} established TCP connections.";
            }

            if (Has(question, "process", "processes", "app", "application", "program"))
            {
                if (!snapshot.ProcessMonitoringAvailable) return null;
                if (snapshot.FlaggedProcessCount <= 0)
                    return "Sentinel does not currently have a flagged process condition to identify.";

                return
                    $"Flagged process finding\n\n" +
                    $"Process: {Friendly(snapshot.PrimaryFlaggedProcessName, "Unknown process")}\n" +
                    $"PID: {(snapshot.PrimaryFlaggedProcessId > 0 ? snapshot.PrimaryFlaggedProcessId.ToString() : "Unavailable")}\n" +
                    $"Reason: {Friendly(snapshot.PrimaryFlaggedProcessReason, "No specific process flag reason was recorded.")}";
            }

            if (Has(question, "service", "services"))
            {
                if (!snapshot.ServiceMonitoringAvailable) return null;
                if (snapshot.FlaggedServiceCount <= 0)
                    return "Sentinel does not currently have a flagged Windows service condition to identify.";

                return
                    $"Flagged service finding\n\n" +
                    $"Service: {Friendly(snapshot.PrimaryFlaggedServiceName, "Unknown service")}\n" +
                    $"Reason: {Friendly(snapshot.PrimaryFlaggedServiceReason, "No specific service flag reason was recorded.")}";
            }

            if (Has(question, "startup", "starts with windows", "startup entry", "startup app"))
            {
                if (!snapshot.StartupPersistenceMonitoringAvailable) return null;
                if (snapshot.FlaggedStartupEntryCount <= 0)
                    return "Sentinel does not currently have a flagged startup-persistence entry to identify.";

                return
                    $"Flagged startup finding\n\n" +
                    $"Entry: {Friendly(snapshot.PrimaryFlaggedStartupEntryName, "Unknown startup entry")}\n" +
                    $"Reason: {Friendly(snapshot.PrimaryFlaggedStartupEntryReason, "No specific startup flag reason was recorded.")}";
            }

            if (Has(question, "scheduled task", "scheduled tasks", "task scheduler"))
            {
                if (!snapshot.ScheduledTaskMonitoringAvailable) return null;
                if (snapshot.FlaggedScheduledTaskCount <= 0)
                    return "Sentinel does not currently have a flagged scheduled task to identify.";

                return
                    $"Flagged scheduled-task finding\n\n" +
                    $"Task: {Friendly(snapshot.PrimaryFlaggedScheduledTaskName, "Unknown scheduled task")}\n" +
                    $"Reason: {Friendly(snapshot.PrimaryFlaggedScheduledTaskReason, "No specific scheduled-task flag reason was recorded.")}";
            }

            if (Has(question, "command line", "command-line", "powershell", "script"))
            {
                if (!snapshot.CommandLineMonitoringAvailable) return null;
                if (snapshot.FlaggedCommandLineCount <= 0)
                    return "Sentinel does not currently have a flagged command-line condition to identify.";

                return
                    $"Flagged command-line finding\n\n" +
                    $"Process: {Friendly(snapshot.PrimaryCommandLineProcessName, "Unknown process")}\n" +
                    $"Command summary: {Friendly(snapshot.PrimaryCommandLineSummary, "Unavailable")}\n" +
                    $"Reason: {Friendly(snapshot.PrimaryCommandLineReason, "No specific command-line flag reason was recorded.")}";
            }

            if (Has(question, "parent process", "child process", "process lineage", "lineage"))
            {
                if (!snapshot.ProcessLineageMonitoringAvailable) return null;
                if (snapshot.FlaggedProcessRelationshipCount <= 0)
                    return "Sentinel does not currently have a flagged parent-child process relationship to identify.";

                return
                    $"Flagged process-lineage finding\n\n" +
                    $"Parent: {Friendly(snapshot.PrimaryLineageParentProcessName, "Unknown parent")}\n" +
                    $"Child: {Friendly(snapshot.PrimaryLineageChildProcessName, "Unknown child")}\n" +
                    $"Reason: {Friendly(snapshot.PrimaryLineageReason, "No specific process-lineage flag reason was recorded.")}";
            }

            if (Has(question, "authentication", "logon", "login", "sign-in", "signin", "failed logon"))
            {
                if (!snapshot.AuthenticationMonitoringAvailable) return null;
                if (!snapshot.AuthenticationAnomalyDetected)
                    return "Sentinel does not currently have a verified authentication anomaly to identify.";

                return
                    $"Authentication finding\n\n" +
                    $"Primary source: {Friendly(snapshot.PrimaryAuthenticationSource, "Unavailable")}\n" +
                    $"Recent failed logons: {snapshot.RecentFailedLogonCount}\n" +
                    $"Reason: {Friendly(snapshot.AuthenticationAnomalySummary, "Sentinel recorded an authentication anomaly but no additional summary is available.")}\n" +
                    $"Confidence: {snapshot.AuthenticationAnomalyConfidenceScore}%.";
            }

            return null;
        }

        private static string Friendly(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string trimmed = value.Trim();
            return trimmed.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                ? fallback
                : trimmed;
        }

        private static string BuildComputerOverviewAnswer(SystemSnapshot snapshot)
        {
            string finding = snapshot.InvestigationRequiresAttention
                ? $"I found a verified condition that needs attention: {Safe(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened)}"
                : "I do not currently see a verified condition that requires your attention.";

            string disk = snapshot.DiskTotalGB > 0
                ? $"disk {snapshot.DiskUsagePercent:0.0}% used with {snapshot.DiskFreeGB:0.0} GB free"
                : "disk capacity unavailable";

            string network = snapshot.NetworkConnectionMonitoringAvailable
                ? $"network monitoring active with {snapshot.FlaggedConnectionCount} flagged connection condition{(snapshot.FlaggedConnectionCount == 1 ? string.Empty : "s")}"
                : $"network monitoring {snapshot.NetworkConnectionMonitoringStatus}";

            string security = $"Defender {snapshot.DefenderStatus}; Firewall {snapshot.FirewallStatus}";

            return
                "Here’s what’s going on with your computer right now.\n\n" +
                $"{finding}\n\n" +
                $"Current snapshot: CPU {snapshot.CpuUsagePercent:0.0}%, memory {snapshot.MemoryUsagePercent:0.0}%, {disk}, {security}, and {network}. " +
                $"Sentinel is monitoring {snapshot.ProcessCount} running processes and {snapshot.EstablishedConnectionCount} established TCP connections.\n\n" +
                "I can go deeper from here. Ask me to:\n" +
                "• explain the current issue and what it means\n" +
                "• check security, suspicious activity, Defender, or Firewall\n" +
                "• check performance, memory, CPU, storage, startup apps, or processes\n" +
                "• review crashes, drivers, Windows Update, or recent investigation history\n" +
                "• search approved external sources or use AI to help explain an unresolved issue (subscription features when required)\n\n" +
                "This is a current evidence snapshot, not proof that no hidden threat exists.";
        }

        private string BuildPerformanceAnswer(SystemSnapshot snapshot)
        {
            PerformanceBaselineService.PerformanceBaselineResult baseline = _performanceBaseline.GetCurrent();
            List<string> contributors = new();

            if (snapshot.CpuUsagePercent >= 85)
                contributors.Add($"CPU use is currently very high at {snapshot.CpuUsagePercent:0.0}%");
            if (snapshot.MemoryUsagePercent >= 90)
                contributors.Add($"memory use is very high at {snapshot.MemoryUsagePercent:0.0}%");
            if (snapshot.DiskTotalGB > 0 && (snapshot.DiskUsagePercent >= 95 || snapshot.DiskFreeGB <= 5))
                contributors.Add($"the Windows drive is critically low on free space ({snapshot.DiskFreeGB:0.0} GB free)");
            if (baseline.IsEstablished && baseline.ProcessCountDeviation)
                contributors.Add($"the running-process count ({snapshot.ProcessCount}) is materially above this computer's established baseline");

            string current = $"Current verified readings are CPU {snapshot.CpuUsagePercent:0.0}%, memory {snapshot.MemoryUsagePercent:0.0}% ({snapshot.MemoryUsedGB:0.00} GB of {snapshot.MemoryTotalGB:0.00} GB), and {snapshot.ProcessCount} running processes.";
            string topProcess = snapshot.HighestMemoryProcessGB > 0
                ? $" The highest-memory process is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB."
                : string.Empty;

            string baselineText = baseline.IsEstablished
                ? $" Sentinel has an established local performance baseline. {baseline.Summary}"
                : $" Sentinel is still learning this computer's normal performance ({baseline.SampleCount}/12 one-minute baseline samples), so it does not yet have enough history to compare today's readings with this PC's normal behavior.";

            if (contributors.Count > 0)
            {
                string joined = string.Join("; ", contributors);
                return $"I found current performance evidence that can contribute to slowness: {joined}. {current}{topProcess}{baselineText} These measurements identify likely current contributors, not a guaranteed single root cause. If the slowdown continues after these readings return to normal, Sentinel should keep collecting evidence rather than blame an unrelated security finding.";
            }

            return $"I do not currently see a verified resource bottleneck that explains the slowdown. {current}{topProcess}{baselineText} A short slowdown can finish before a monitoring snapshot is taken, so this does not prove that nothing happened. Sentinel does not have enough verified evidence to name a cause right now, and it will not substitute an unrelated security-monitoring warning as the explanation. Keep Sentinel running so later samples can be compared with this computer's normal baseline.";
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

        private static bool IsBroadComputerOverviewQuestion(string value)
        {
            return Has(value,
                "tell me what's going on with my computer",
                "tell me whats going on with my computer",
                "tell me what is going on with my computer",
                "what's going on with my computer",
                "whats going on with my computer",
                "what is going on with my computer",
                "tell me what's going on with my pc",
                "tell me whats going on with my pc",
                "tell me what is going on with my pc",
                "what's going on with my pc",
                "whats going on with my pc",
                "what is going on with my pc",
                "how is my computer",
                "how's my computer",
                "hows my computer",
                "how is my pc",
                "how's my pc",
                "hows my pc",
                "computer status",
                "pc status",
                "status of my computer",
                "status of my pc",
                "check my computer",
                "check my pc");
        }

        private static bool IsWindowsUpdateQuestion(string value) =>
            Has(value, "windows update", "windows updates", "update status", "check for updates", "latest update", "latest updates", "up to date", "fully updated", "updates installed", "missing updates", "available updates", "need updates", "need an update", "current on updates");

        private static bool IsPendingRestartQuestion(string value)
        {
            if (Has(value, "pending restart", "restart pending", "restart required", "reboot required", "need to restart", "need a restart", "need restart", "needs to restart", "needs a restart", "needs restart", "need to reboot", "waiting for restart", "waiting on restart", "restart it's waiting", "restart it is waiting", "should i restart", "should i reboot"))
                return true;

            bool restartTopic = value.Contains("restart", StringComparison.OrdinalIgnoreCase) ||
                                value.Contains("reboot", StringComparison.OrdinalIgnoreCase);
            bool pendingIntent = Has(value, "pending", "waiting", "required", "requires", "needed", "needs", "what caused", "why");
            return restartTopic && pendingIntent;
        }

        private static bool IsStartupLogonPerformanceQuestion(string value)
        {
            bool localMachine = Has(value, "my computer", "my pc", "this computer", "this pc", "windows");
            bool startupTopic = Has(value,
                "log in", "login", "log on", "logon", "sign in", "signin", "sign-in",
                "boot", "boot up", "startup", "start up", "starting windows", "windows start");
            bool delayIntent = Has(value,
                "slow", "takes", "taking", "long", "minutes", "delay", "delayed", "hang", "stuck", "waiting");

            return startupTopic && (delayIntent || localMachine);
        }

        private static bool IsPerformanceQuestion(string value) =>
            Has(value, "computer slow", "pc slow", "running slow", "running slowly", "feels slow", "sluggish", "lagging", "laggy", "performance problem", "performance issue", "why is my computer slow", "why is my pc slow");

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
