/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Builds memory-pressure context without treating Windows Memory Compression
    /// as a suspicious application. This monitor is read-only and intended to
    /// explain what is actually consuming memory before Sentinel recommends action.
    /// </summary>
    public sealed class MemoryInvestigationMonitor
    {
        private const double ElevatedMemoryPercent = 85.0;
        private const double HighMemoryPercent = 92.0;
        private const int ContributorCount = 3;

        public MemoryInvestigationSnapshot GetSnapshot(double memoryUsagePercent)
        {
            Process[] processes = Process.GetProcesses();
            List<MemoryContributor> contributors = new();
            double memoryCompressionGB = 0;

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        string name = process.ProcessName;
                        double workingSetGB = process.WorkingSet64 / 1024d / 1024d / 1024d;

                        if (IsMemoryCompression(name))
                        {
                            memoryCompressionGB += workingSetGB;
                            continue;
                        }

                        if (workingSetGB > 0)
                        {
                            contributors.Add(new MemoryContributor(name, workingSetGB));
                        }
                    }
                    catch
                    {
                        // Protected and exited processes are skipped safely.
                    }
                }

                MemoryContributor[] top = contributors
                    .OrderByDescending(item => item.WorkingSetGB)
                    .Take(ContributorCount)
                    .ToArray();

                string topSummary = top.Length == 0
                    ? "No individual application memory contributors could be read."
                    : string.Join(", ", top.Select(item => $"{item.ProcessName} {item.WorkingSetGB:0.00} GB"));

                MemoryPressureLevel pressure = memoryUsagePercent >= HighMemoryPercent
                    ? MemoryPressureLevel.High
                    : memoryUsagePercent >= ElevatedMemoryPercent
                        ? MemoryPressureLevel.Elevated
                        : MemoryPressureLevel.Normal;

                string conclusion = pressure switch
                {
                    MemoryPressureLevel.High => "Memory use is high enough to investigate for sustained pressure.",
                    MemoryPressureLevel.Elevated => "Memory use is elevated, but the current level alone does not indicate a problem.",
                    _ => "Memory use is within Sentinel's normal monitoring range."
                };

                string recommendation = pressure switch
                {
                    MemoryPressureLevel.High => "Sentinel should watch whether memory remains above 92% and whether one application continues growing before recommending that anything be closed.",
                    MemoryPressureLevel.Elevated => "No immediate action is required. Sentinel should continue monitoring the largest applications and Windows memory management.",
                    _ => "No action is required."
                };

                return new MemoryInvestigationSnapshot(
                    pressure,
                    Math.Round(memoryUsagePercent, 1),
                    Math.Round(memoryCompressionGB, 2),
                    topSummary,
                    conclusion,
                    recommendation);
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private static bool IsMemoryCompression(string processName) =>
            processName.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("MemoryCompression", StringComparison.OrdinalIgnoreCase);

        private sealed record MemoryContributor(string ProcessName, double WorkingSetGB);

        public enum MemoryPressureLevel
        {
            Normal,
            Elevated,
            High
        }

        public sealed record MemoryInvestigationSnapshot(
            MemoryPressureLevel PressureLevel,
            double MemoryUsagePercent,
            double MemoryCompressionGB,
            string TopContributors,
            string Conclusion,
            string Recommendation);
    }
}
