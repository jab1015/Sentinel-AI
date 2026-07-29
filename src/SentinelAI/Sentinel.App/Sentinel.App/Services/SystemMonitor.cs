/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    public class SystemMonitor
    {
        private readonly Random _random = new();

        public double GetCpuUsage()
        {
            // Temporary implementation.
            // Will be replaced with native GetSystemTimes monitoring.
            return Math.Round(_random.NextDouble() * 45.0 + 5.0, 1);
        }

        public double GetMemoryUsedGB()
        {
            var info = GC.GetGCMemoryInfo();

            if (info.TotalAvailableMemoryBytes <= 0)
                return 0;

            double used = GC.GetTotalMemory(false) / 1024d / 1024d / 1024d;

            return Math.Round(used, 2);
        }

        public double GetMemoryTotalGB()
        {
            var info = GC.GetGCMemoryInfo();

            if (info.TotalAvailableMemoryBytes <= 0)
                return 0;

            return Math.Round(
                info.TotalAvailableMemoryBytes / 1024d / 1024d / 1024d,
                2);
        }

        public double GetMemoryPercent()
        {
            var total = GetMemoryTotalGB();

            if (total <= 0)
                return 0;

            return Math.Round(GetMemoryUsedGB() / total * 100.0, 1);
        }
    }
}