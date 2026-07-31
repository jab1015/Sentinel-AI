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
    /// samples before Sentinel elevates it, and repeated process growth is required
    /// before Sentinel recommends action against a specific application.
    /// </summary>
    public sealed class MemoryInvestigationMonitor
    {
        private const double ElevatedMemoryPercent = 85.0;
        private const double HighMemoryPercent = 92.0;
        private const double MeaningfulProcessGrowthGB = 0.25;
        private const int SustainedHighSampleCount = 3;
        private const int RepeatedGrowthSampleCount = 2;
        private const int ContributorCount = 3;

        private int _consecutiveHighSamples;
        private Dictionary<string, double> _previousWorkingSets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _growthStreaks = new(StringComparer.OrdinalIgnoreCase);

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

                        if (workingSetGB <= 0) continue;

                        currentWorkingSets[name] = currentWorkingSets.TryGetValue(name, out double existing)
                            ? existing + workingSetGB
                            : workingSetGB;
                    }
                    catch
                    {
                        // Protected and exited processes are skipped safely.
                    }
                }

                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
                foreach ((string processName, double workingSetGB) in currentWorkingSets)
                {
                    double growthGB = _previousWorkingSets.TryGetValue(processName, out double previous)
                        ? workingSetGB - previous
                        : 0;

                    int growthStreak = growthGB >= MeaningfulProcessGrowthGB
                        ? (_growthStreaks.TryGetValue(processName, out int existingStreak) ? existingStreak + 1 : 1)
                        : 0;

                    _growthStreaks[processName] = growthStreak;
                    seen.Add(processName);
                    contributors.Add(new MemoryContributor(processName, workingSetGB, growthGB, growthStreak));
                }

                foreach (string processName in _growthStreaks.Keys.Where(name => !seen.Contains(name)).ToArray())
                {
                    _growthStreaks.Remove(processName);
                }

                MemoryContributor[] top = contributors
                    .OrderByDescending(item => item.WorkingSetGB)
                    .Take(ContributorCount)
                    .ToArray();

                MemoryContributor? repeatedlyGrowing = contributors
                    .Where(item => item.GrowthStreak >= RepeatedGrowthSampleCount)
                    .OrderByDescending(item => item.GrowthStreak)
                    .ThenByDescending(item => item.GrowthGB)
                    .FirstOrDefault();

                string topSummary = top.Length == 0
                    ? "No individual application memory contributors could be read."
                    : string.Join(", ", top.Select(FormatContributor));

                bool sustainedHigh = _consecutiveHighSamples >= SustainedHighSampleCount;
                MemoryPressureLevel pressure = sustainedHigh
                    ? MemoryPressureLevel.High
                    : memoryUsagePercent >= ElevatedMemoryPercent
                        ? MemoryPressureLevel.Elevated
                        : MemoryPressureLevel.Normal;

                string conclusion;
                string recommendation;

                if (sustainedHigh && repeatedlyGrowing is not null)
                {
                    conclusion = $"Memory remained above {HighMemoryPercent:0}% for {_consecutiveHighSamples} consecutive checks, and {repeatedlyGrowing.ProcessName} showed meaningful growth for {repeatedlyGrowing.GrowthStreak} consecutive comparisons.";
                    recommendation = $"Sentinel correlated sustained system memory pressure with repeated growth from {repeatedlyGrowing.ProcessName}. Review that application first. Windows Memory Compression itself should not be stopped.";
                }
                else if (sustainedHigh)
                {
                    conclusion = $"Memory remained above {HighMemoryPercent:0}% for {_consecutiveHighSamples} consecutive checks. Sentinel confirmed sustained memory pressure, but no application has yet shown repeated meaningful growth.";
                    recommendation = "No specific application should be closed yet. Sentinel will continue correlating the largest contributors and only recommend process action when repeated growth provides stronger evidence.";
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
                foreach (Process process in processes) process.Dispose();
            }
        }

        private static string FormatContributor(MemoryContributor contributor)
        {
            if (contributor.GrowthStreak >= RepeatedGrowthSampleCount)
                return $"{contributor.ProcessName} {contributor.WorkingSetGB:0.00} GB (+{contributor.GrowthGB:0.00} GB, growing {contributor.GrowthStreak} checks)";

            if (contributor.GrowthGB >= MeaningfulProcessGrowthGB)
                return $"{contributor.ProcessName} {contributor.WorkingSetGB:0.00} GB (+{contributor.GrowthGB:0.00} GB)";

            return $"{contributor.ProcessName} {contributor.WorkingSetGB:0.00} GB";
        }

        private static bool IsMemoryCompression(string processName) =>
            processName.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("MemoryCompression", StringComparison.OrdinalIgnoreCase);

        private sealed record MemoryContributor(string ProcessName, double WorkingSetGB, double GrowthGB, int GrowthStreak);

        public enum MemoryPressureLevel { Normal, Elevated, High }

        public sealed record MemoryInvestigationSnapshot(
            MemoryPressureLevel PressureLevel,
            double MemoryUsagePercent,
            double MemoryCompressionGB,
            string TopContributors,
            string Conclusion,
            string Recommendation);
    }
}
