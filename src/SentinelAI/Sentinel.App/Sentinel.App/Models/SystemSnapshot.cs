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
    }
}
