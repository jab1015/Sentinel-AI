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
    /// samples before Sentinel elevates it, and process growth is correlated before
    /// Sentinel suggests that a particular application may be responsible.
    /// </summary>
    public sealed class MemoryInvestigationMonitor
    {
        private const double ElevatedMemoryPercent = 85.0;
        private const double HighMemoryPercent = 92.0;
        private const double MeaningfulProcessGrowthGB = 0.25;
        private const int SustainedHighSampleCount = 3;
        private const int ContributorCount = 3;

        private int _consecutiveHighSamples;
        private Dictionary<string, double> _previousWorkingSets = new(StringComparer.OrdinalIgnoreCase);

        public MemoryInvestigationSnapshot GetSnapshot(double memoryUsagePercent)
        {
            _consecutiveHighSamples = memoryUsagePercent >= HighMemoryPercent
                ? _consecutiveHighSamples + 1
                : 0;

            Process[] processes = Process.GetProcesses();
            List<MemoryContributor> contributors = new();
            Dictionary<string, double> currentWorkingSets = new(StringComparer.OrdinalIgnoreCase);
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

                        if (workingSetGB <= 0)
                        {
                            continue;
                        }

                        currentWorkingSets[name] = currentWorkingSets.TryGetValue(name, out double existing)
                            ? existing + workingSetGB
                            : workingSetGB;
                    }
                    catch
                    {
                        // Protected and exited processes are skipped safely.
                    }
                }

                foreach ((string processName, double workingSetGB) in currentWorkingSets)
                {
                    double growthGB = _previousWorkingSets.TryGetValue(processName, out double previous)
                        ? workingSetGB - previous
                        : 0;

                    contributors.Add(new MemoryContributor(processName, workingSetGB, growthGB));
                }

                MemoryContributor[] top = contributors
                    .OrderByDescending(item => item.WorkingSetGB)
                    .Take(ContributorCount)
                    .ToArray();

                MemoryContributor? fastestGrowing = contributors
                    .Where(item => item.GrowthGB >= MeaningfulProcessGrowthGB)
                    .OrderByDescending(item => item.GrowthGB)
                    .FirstOrDefault();

                string topSummary = top.Length == 0
                    ? "No individual application memory contributors could be read."
                    : string.Join(", ", top.Select(item => FormatContributor(item)));

                bool sustainedHigh = _consecutiveHighSamples >= SustainedHighSampleCount;
                MemoryPressureLevel pressure = sustainedHigh
                    ? MemoryPressureLevel.High
                    : memoryUsagePercent >= ElevatedMemoryPercent
                        ? MemoryPressureLevel.Elevated
                        : MemoryPressureLevel.Normal;

                string conclusion;
                string recommendation;

                if (sustainedHigh && fastestGrowing is not null)
                {
                    conclusion = $"Memory remained above {HighMemoryPercent:0}% for {_consecutiveHighSamples} consecutive checks, and {fastestGrowing.ProcessName} increased by {fastestGrowing.GrowthGB:0.00} GB since the prior memory sample.";
                    recommendation = $"Sentinel identified {fastestGrowing.ProcessName} as the fastest-growing application during sustained memory pressure. Continue monitoring for repeated growth before closing it; Windows Memory Compression itself should not be stopped.";
                }
                else if (sustainedHigh)
                {
                    conclusion = $"Memory remained above {HighMemoryPercent:0}% for {_consecutiveHighSamples} consecutive checks. Sentinel confirmed sustained memory pressure, but no single application showed meaningful growth in the latest comparison.";
                    recommendation = "Review the largest application contributors shown by Sentinel. Do not close Windows Memory Compression; Sentinel will continue comparing application growth before recommending a specific process action.";
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

                _previousWorkingSets = currentWorkingSets;

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

        private static string FormatContributor(MemoryContributor contributor)
        {
            if (contributor.GrowthGB >= MeaningfulProcessGrowthGB)
            {
                return $"{contributor.ProcessName} {contributor.WorkingSetGB:0.00} GB (+{contributor.GrowthGB:0.00} GB)";
            }

            return $"{contributor.ProcessName} {contributor.WorkingSetGB:0.00} GB";
        }

        private static bool IsMemoryCompression(string processName) =>
            processName.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("MemoryCompression", StringComparison.OrdinalIgnoreCase);

        private sealed record MemoryContributor(string ProcessName, double WorkingSetGB, double GrowthGB);

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
