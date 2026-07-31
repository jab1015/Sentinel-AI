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
    /// as a suspicious application. High memory must persist across multiple
    /// samples before Sentinel elevates it to an attention condition.
    /// </summary>
    public sealed class MemoryInvestigationMonitor
    {
        private const double ElevatedMemoryPercent = 85.0;
        private const double HighMemoryPercent = 92.0;
        private const int SustainedHighSampleCount = 3;
        private const int ContributorCount = 3;

        private int _consecutiveHighSamples;

        public MemoryInvestigationSnapshot GetSnapshot(double memoryUsagePercent)
        {
            if (memoryUsagePercent >= HighMemoryPercent)
            {
                _consecutiveHighSamples++;
            }
            else
            {
                _consecutiveHighSamples = 0;
            }

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

                bool sustainedHigh = _consecutiveHighSamples >= SustainedHighSampleCount;

                MemoryPressureLevel pressure = sustainedHigh
                    ? MemoryPressureLevel.High
                    : memoryUsagePercent >= ElevatedMemoryPercent
                        ? MemoryPressureLevel.Elevated
                        : MemoryPressureLevel.Normal;

                string conclusion;
                string recommendation;

                if (sustainedHigh)
                {
                    conclusion = $"Memory remained above {HighMemoryPercent:0}% for {_consecutiveHighSamples} consecutive checks. Sentinel confirmed sustained memory pressure.";
                    recommendation = "Review the largest application contributors shown by Sentinel. Do not close Windows Memory Compression; Sentinel will continue checking whether one application keeps growing before recommending a specific process action.";
                }
                else if (memoryUsagePercent >= HighMemoryPercent)
                {
                    conclusion = $"Memory is currently high, but Sentinel has only confirmed {_consecutiveHighSamples} of {SustainedHighSampleCount} required consecutive checks.";
                    recommendation = "No immediate action is required. Sentinel is verifying whether this is temporary or sustained before interrupting you.";
                }
                else if (pressure == MemoryPressureLevel.Elevated)
                {
                    conclusion = "Memory use is elevated, but the current level alone does not indicate a problem.";
                    recommendation = "No immediate action is required. Sentinel will continue monitoring the largest applications and Windows memory management.";
                }
                else
                {
                    conclusion = "Memory use is within Sentinel's normal monitoring range.";
                    recommendation = "No action is required.";
                }

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
