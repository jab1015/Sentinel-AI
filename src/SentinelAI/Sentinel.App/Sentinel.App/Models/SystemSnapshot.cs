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
    }
}
