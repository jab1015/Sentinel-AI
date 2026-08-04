/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Persists a rolling memory-pressure history so Sentinel can distinguish a
    /// transient spike from sustained pressure and repeated high-memory process use.
    /// This service records evidence only and never changes a process.
    /// </summary>
    public sealed class MemoryPressureHistoryService
    {
        private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(24);
        private static readonly TimeSpan MinimumSampleSpacing = TimeSpan.FromMinutes(10);
        private readonly string _historyPath;
        private readonly MemoryPressureAssessmentService _assessmentService = new();

        public MemoryPressureHistoryService()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods",
                "Sentinel AI");

            Directory.CreateDirectory(directory);
            _historyPath = Path.Combine(directory, "memory-pressure-history.json");
        }

        public MemoryPressureTrend RecordAndAssess()
        {
            MemoryPressureAssessment assessment = _assessmentService.Assess();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<MemoryPressureHistorySample> samples = Load()
                .Where(sample => now - sample.TimestampUtc <= RetentionWindow)
                .OrderBy(sample => sample.TimestampUtc)
                .ToList();

            if (assessment.MemoryStatusVerified &&
                (samples.Count == 0 || now - samples[^1].TimestampUtc >= MinimumSampleSpacing))
            {
                samples.Add(new MemoryPressureHistorySample(
                    now,
                    assessment.UsedPercent,
                    assessment.AvailablePhysicalBytes,
                    assessment.LargestProcesses
                        .Where(process => !process.IsSentinel && !process.IsSystemLike)
                        .Take(10)
                        .Select(process => new MemoryProcessHistorySample(
                            process.ProcessName,
                            process.WorkingSetBytes))
                        .ToArray()));

                Save(samples);
            }

            MemoryPressureHistorySample[] recent = samples
                .Where(sample => now - sample.TimestampUtc <= TimeSpan.FromHours(2))
                .ToArray();

            int highPressureSamples = recent.Count(sample =>
                sample.UsedPercent >= 90d &&
                sample.AvailablePhysicalBytes <= 2UL * 1024 * 1024 * 1024);

            bool sustainedPressure = recent.Length >= 3 && highPressureSamples >= 3;

            MemoryProcessTrend[] processTrends = recent
                .SelectMany(sample => sample.Processes.Select(process => new
                {
                    sample.TimestampUtc,
                    process.ProcessName,
                    process.WorkingSetBytes
                }))
                .GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new MemoryProcessTrend(
                    group.Key,
                    group.Count(),
                    group.Average(item => item.WorkingSetBytes),
                    group.Max(item => item.WorkingSetBytes),
                    group.Count() >= 3 && group.Average(item => item.WorkingSetBytes) >= 750L * 1024 * 1024))
                .OrderByDescending(item => item.AverageWorkingSetBytes)
                .ToArray();

            bool repeatedLargeProcess = processTrends.Any(item => item.RepeatedHighUsage);

            string summary;
            if (!assessment.MemoryStatusVerified)
                summary = "Sentinel could not verify current memory pressure, so no memory remediation decision is allowed.";
            else if (!sustainedPressure)
                summary = "Memory pressure has not remained high long enough to justify remediation. Sentinel will continue observing quietly.";
            else if (!repeatedLargeProcess)
                summary = "Sustained memory pressure is verified, but Sentinel has not identified a repeatedly large non-system process strongly enough to justify remediation.";
            else
                summary = "Sentinel verified sustained memory pressure and repeated high memory use by one or more non-system processes. A conservative remediation plan can now be evaluated.";

            return new MemoryPressureTrend(
                assessment,
                recent,
                sustainedPressure,
                processTrends,
                sustainedPressure && repeatedLargeProcess,
                summary);
        }

        private List<MemoryPressureHistorySample> Load()
        {
            try
            {
                if (!File.Exists(_historyPath))
                    return new List<MemoryPressureHistorySample>();

                string json = File.ReadAllText(_historyPath);
                return JsonSerializer.Deserialize<List<MemoryPressureHistorySample>>(json)
                    ?? new List<MemoryPressureHistorySample>();
            }
            catch
            {
                return new List<MemoryPressureHistorySample>();
            }
        }

        private void Save(IReadOnlyList<MemoryPressureHistorySample> samples)
        {
            try
            {
                string json = JsonSerializer.Serialize(samples, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_historyPath, json);
            }
            catch
            {
                // Failure to persist history means less evidence, never permission
                // to perform a more aggressive memory action.
            }
        }
    }

    public sealed record MemoryPressureTrend(
        MemoryPressureAssessment CurrentAssessment,
        IReadOnlyList<MemoryPressureHistorySample> RecentSamples,
        bool SustainedPressureVerified,
        IReadOnlyList<MemoryProcessTrend> ProcessTrends,
        bool RemediationPlanningWarranted,
        string Summary);

    public sealed record MemoryPressureHistorySample(
        DateTimeOffset TimestampUtc,
        double UsedPercent,
        ulong AvailablePhysicalBytes,
        IReadOnlyList<MemoryProcessHistorySample> Processes);

    public sealed record MemoryProcessHistorySample(
        string ProcessName,
        long WorkingSetBytes);

    public sealed record MemoryProcessTrend(
        string ProcessName,
        int SampleCount,
        double AverageWorkingSetBytes,
        long PeakWorkingSetBytes,
        bool RepeatedHighUsage);
}
