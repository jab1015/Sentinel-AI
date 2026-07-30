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
        public int InstalledServiceCount { get; set; }
        public int RunningServiceCount { get; set; }
        public int FlaggedServiceCount { get; set; }
        public string PrimaryFlaggedServiceName { get; set; } = "None";
        public string PrimaryFlaggedServiceReason { get; set; } = "No service warning conditions were detected.";
        public bool DefenderEnabled { get; set; }
        public bool FirewallEnabled { get; set; }
        public string DefenderStatus { get; set; } = "Loading...";
        public string FirewallStatus { get; set; } = "Loading...";
        public int CriticalEventCount { get; set; }
        public int ErrorEventCount { get; set; }
        public DateTime? LatestEventTime { get; set; }
        public string LatestEventSource { get; set; } = "None";
        public string LatestEventMessage { get; set; } =
            "No critical or error events detected in the last 24 hours.";
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
    }
}
