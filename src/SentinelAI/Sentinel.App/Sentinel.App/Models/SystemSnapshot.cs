/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Models
{
    public class SystemSnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double CpuUsagePercent { get; set; }
        public double MemoryUsedGB { get; set; }
        public double MemoryTotalGB { get; set; }
        public double MemoryUsagePercent { get; set; }
        public string MemoryPressureLevel { get; set; } = "Normal";
        public double MemoryCompressionGB { get; set; }
        public string MemoryTopContributors { get; set; } = "No memory investigation data available.";
        public string MemoryConclusion { get; set; } = "Memory use is within Sentinel's normal monitoring range.";
        public string MemoryRecommendation { get; set; } = "No action is required.";
        public double DiskUsagePercent { get; set; }
        public double DiskFreeGB { get; set; }
        public double DiskTotalGB { get; set; }
        public double DownloadMbps { get; set; }
        public double UploadMbps { get; set; }
        public int ProcessCount { get; set; }
        public string HighestMemoryProcessName { get; set; } = "Unknown";
        public double HighestMemoryProcessGB { get; set; }
        public int FlaggedProcessCount { get; set; }
        public string PrimaryFlaggedProcessName { get; set; } = "None";
        public string PrimaryFlaggedProcessReason { get; set; } = "No process warning conditions were detected.";
        public int ProcessRelationshipCount { get; set; }
        public int FlaggedProcessRelationshipCount { get; set; }
        public string PrimaryLineageChildProcessName { get; set; } = "None";
        public string PrimaryLineageParentProcessName { get; set; } = "None";
        public string PrimaryLineageReason { get; set; } = "No unusual parent-child process relationships were detected.";
        public int ReviewedCommandLineProcessCount { get; set; }
        public int FlaggedCommandLineCount { get; set; }
        public string PrimaryCommandLineProcessName { get; set; } = "None";
        public string PrimaryCommandLineReason { get; set; } = "No unusual command-line combinations were detected.";
        public string PrimaryCommandLineSummary { get; set; } = "None";
        public int InstalledServiceCount { get; set; }
        public int RunningServiceCount { get; set; }
        public int FlaggedServiceCount { get; set; }
        public string PrimaryFlaggedServiceName { get; set; } = "None";
        public string PrimaryFlaggedServiceReason { get; set; } = "No service warning conditions were detected.";
        public int StartupEntryCount { get; set; }
        public int FlaggedStartupEntryCount { get; set; }
        public string PrimaryFlaggedStartupEntryName { get; set; } = "None";
        public string PrimaryFlaggedStartupEntryReason { get; set; } = "No unusual startup persistence entries were detected.";
        public int ScheduledTaskCount { get; set; }
        public int FlaggedScheduledTaskCount { get; set; }
        public string PrimaryFlaggedScheduledTaskName { get; set; } = "None";
        public string PrimaryFlaggedScheduledTaskReason { get; set; } = "No unusual scheduled-task persistence was detected.";
        public int EstablishedConnectionCount { get; set; }
        public int ExternalConnectionCount { get; set; }
        public int FlaggedConnectionCount { get; set; }
        public string PrimaryFlaggedConnectionProcessName { get; set; } = "None";
        public string PrimaryFlaggedConnectionRemoteEndpoint { get; set; } = "None";
        public string PrimaryFlaggedConnectionReason { get; set; } = "No unusual active TCP connections were detected.";
        public int ListeningTcpEndpointCount { get; set; }
        public int UdpEndpointCount { get; set; }
        public int AttributedExternalConnectionCount { get; set; }
        public bool NetworkConnectionMonitoringAvailable { get; set; }
        public string NetworkConnectionMonitoringStatus { get; set; } = "Starting";
        public bool DefenderEnabled { get; set; }
        public bool FirewallEnabled { get; set; }
        public string DefenderStatus { get; set; } = "Loading...";
        public string FirewallStatus { get; set; } = "Loading...";
        public int CriticalEventCount { get; set; }
        public int ErrorEventCount { get; set; }
        public DateTime? LatestEventTime { get; set; }
        public string LatestEventSource { get; set; } = "None";
        public string LatestEventMessage { get; set; } = "No critical or error events detected in the last 24 hours.";
        public int RiskScore { get; set; }
        public string RiskLevel { get; set; } = "Calculating...";
        public string RiskSummary { get; set; } = "Analyzing current conditions.";
        public string Recommendation { get; set; } = "Waiting for monitoring data.";
        public string GuidanceTitle { get; set; } = "Analyzing your computer";
        public string GuidanceSeverity { get; set; } = "Loading";
        public int GuidanceConfidencePercent { get; set; }
        public string GuidanceConfidenceLabel { get; set; } = "Collecting evidence";
        public string GuidanceEvidence { get; set; } = "Sentinel AI is collecting evidence for this recommendation.";
        public string GuidanceWhatHappened { get; set; } = "Sentinel AI is reviewing current conditions.";
        public string GuidanceWhyItMatters { get; set; } = "Waiting for enough information to explain the result.";
        public string GuidanceRecommendedAction { get; set; } = "Please wait while monitoring starts.";
        public string GuidanceFixAvailability { get; set; } = "Checking";
        public string GuidanceFixDetails { get; set; } = "Sentinel AI is determining whether a safe fix is available.";
        public string GuidanceActionId { get; set; } = string.Empty;
        public string GuidanceActionLabel { get; set; } = string.Empty;
        public string InvestigationState { get; set; } = "Investigating";
        public string InvestigationConclusion { get; set; } = "Analyzing your computer.";
        public string InvestigationSummary { get; set; } = "Sentinel is reviewing available evidence.";
        public bool InvestigationRequiresAttention { get; set; }
        public string InvestigationReasonCode { get; set; } = "initializing";
        public bool RemediationAvailable { get; set; }
        public bool RemediationRequiresUserApproval { get; set; }
        public string RemediationAction { get; set; } = "None";
        public string RemediationTarget { get; set; } = "None";
        public string RemediationSummary { get; set; } = "No remediation is required.";
        public bool AutonomousProtectionCanExecute { get; set; }
        public bool AutonomousProtectionRequiresUserApproval { get; set; }
        public string AutonomousProtectionAction { get; set; } = "None";
        public string AutonomousProtectionTarget { get; set; } = "None";
        public string AutonomousProtectionSummary { get; set; } = "No autonomous protection action is required.";
        public bool AutonomousProtectionAttempted { get; set; }
        public bool AutonomousProtectionSucceeded { get; set; }
        public DateTimeOffset? AutonomousProtectionCompletedAt { get; set; }
        public string AutonomousProtectionOutcomeTitle { get; set; } = string.Empty;
        public string AutonomousProtectionOutcomeSummary { get; set; } = string.Empty;
        public bool RemediationAttempted { get; set; }
        public bool RemediationSucceeded { get; set; }
        public DateTimeOffset? RemediationCompletedAt { get; set; }
        public string RemediationOutcomeTitle { get; set; } = string.Empty;
        public string RemediationOutcomeSummary { get; set; } = string.Empty;
        public int InvestigationRecurrenceCount { get; set; }
        public bool InvestigationIsRecurring { get; set; }
        public bool InvestigationShouldEscalate { get; set; }
    }
}
