/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Maintains a rolling local baseline so optimization decisions are based on
    /// this computer's normal behavior instead of generic tuning assumptions.
    /// This service is observational only; it never changes Windows settings.
    /// </summary>
    public sealed class PerformanceBaselineService
    {
        private const int MaximumSamples = 720;
        private readonly Queue<PerformanceSample> _samples = new();
        private readonly object _sync = new();

        public PerformanceBaselineResult Record(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            PerformanceSample sample = new(
                snapshot.Timestamp,
                Clamp(snapshot.CpuUsagePercent),
                Clamp(snapshot.MemoryUsagePercent),
                Clamp(snapshot.DiskUsagePercent),
                Math.Max(snapshot.ProcessCount, 0),
                Math.Max(snapshot.DownloadMbps, 0),
                Math.Max(snapshot.UploadMbps, 0));

            lock (_sync)
            {
                _samples.Enqueue(sample);
                while (_samples.Count > MaximumSamples)
                    _samples.Dequeue();

                return BuildResult(sample, _samples.ToArray());
            }
        }

        public PerformanceBaselineResult GetCurrent()
        {
            lock (_sync)
            {
                PerformanceSample[] samples = _samples.ToArray();
                if (samples.Length == 0)
                    return PerformanceBaselineResult.NotReady;

                return BuildResult(samples[^1], samples);
            }
        }

        private static PerformanceBaselineResult BuildResult(
            PerformanceSample current,
            IReadOnlyCollection<PerformanceSample> samples)
        {
            double avgCpu = samples.Average(x => x.CpuPercent);
            double avgMemory = samples.Average(x => x.MemoryPercent);
            double avgDisk = samples.Average(x => x.DiskUsedPercent);
            double avgProcesses = samples.Average(x => x.ProcessCount);

            bool cpuDeviation = samples.Count >= 12 && current.CpuPercent >= Math.Max(75, avgCpu + 30);
            bool memoryDeviation = samples.Count >= 12 && current.MemoryPercent >= Math.Max(82, avgMemory + 20);
            bool diskPressure = current.DiskUsedPercent >= 90;
            bool processDeviation = samples.Count >= 12 && current.ProcessCount >= avgProcesses + Math.Max(35, avgProcesses * 0.30);

            int deviationCount = new[] { cpuDeviation, memoryDeviation, diskPressure, processDeviation }
                .Count(value => value);

            PerformanceBaselineState state = deviationCount switch
            {
                >= 2 => PerformanceBaselineState.Degraded,
                1 => PerformanceBaselineState.Observe,
                _ => PerformanceBaselineState.Normal
            };

            bool enoughHistory = samples.Count >= 12;
            string summary = !enoughHistory
                ? $"Sentinel is learning this computer's normal performance ({samples.Count}/12 baseline samples)."
                : state switch
                {
                    PerformanceBaselineState.Degraded => "Current performance differs materially from this computer's normal baseline.",
                    PerformanceBaselineState.Observe => "One performance measure is outside this computer's normal baseline.",
                    _ => "Current performance is within this computer's normal baseline."
                };

            return new PerformanceBaselineResult(
                state,
                samples.Count,
                enoughHistory,
                avgCpu,
                avgMemory,
                avgDisk,
                avgProcesses,
                current.CpuPercent,
                current.MemoryPercent,
                current.DiskUsedPercent,
                current.ProcessCount,
                cpuDeviation,
                memoryDeviation,
                diskPressure,
                processDeviation,
                summary);
        }

        private static double Clamp(double value) => Math.Clamp(value, 0, 100);

        private sealed record PerformanceSample(
            DateTime Timestamp,
            double CpuPercent,
            double MemoryPercent,
            double DiskUsedPercent,
            int ProcessCount,
            double DownloadMbps,
            double UploadMbps);

        public sealed record PerformanceBaselineResult(
            PerformanceBaselineState State,
            int SampleCount,
            bool IsEstablished,
            double AverageCpuPercent,
            double AverageMemoryPercent,
            double AverageDiskUsedPercent,
            double AverageProcessCount,
            double CurrentCpuPercent,
            double CurrentMemoryPercent,
            double CurrentDiskUsedPercent,
            int CurrentProcessCount,
            bool CpuDeviation,
            bool MemoryDeviation,
            bool DiskPressure,
            bool ProcessCountDeviation,
            string Summary)
        {
            public static PerformanceBaselineResult NotReady { get; } = new(
                PerformanceBaselineState.Learning,
                0,
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                false,
                false,
                "Sentinel is learning this computer's normal performance.");
        }

        public enum PerformanceBaselineState
        {
            Learning,
            Normal,
            Observe,
            Degraded
        }
    }
}
