/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Maintains a rolling local baseline so optimization decisions are based on
    /// this computer's normal behavior instead of generic tuning assumptions.
    /// Baseline history is persisted locally so ordinary app restarts do not force
    /// Sentinel to relearn the same computer from zero. This service is observational
    /// only; it never changes Windows settings.
    /// </summary>
    public sealed class PerformanceBaselineService
    {
        private const int MaximumSamples = 720;
        private const int PersistenceSchemaVersion = 1;
        private static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromMinutes(1);
        private readonly Queue<PerformanceSample> _samples = new();
        private readonly object _sync = new();
        private readonly string _persistencePath;

        public PerformanceBaselineService()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods",
                "Sentinel AI",
                "performance-baseline.json"))
        {
        }

        internal PerformanceBaselineService(string persistencePath)
        {
            if (string.IsNullOrWhiteSpace(persistencePath))
                throw new ArgumentException("A performance-baseline persistence path is required.", nameof(persistencePath));

            _persistencePath = Path.GetFullPath(persistencePath);
            LoadPersistedSamples();
        }

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
                PerformanceSample[] existing = _samples.ToArray();
                if (existing.Length > 0 &&
                    sample.Timestamp - existing[^1].Timestamp < MinimumSampleInterval)
                {
                    // Monitoring cadence may accelerate for security reasons. Do not let
                    // those rapid checks masquerade as independent performance history.
                    return BuildResult(sample, existing);
                }

                _samples.Enqueue(sample);
                while (_samples.Count > MaximumSamples)
                    _samples.Dequeue();

                PersistSamplesBestEffort(_samples.ToArray());
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

        private void LoadPersistedSamples()
        {
            try
            {
                if (!File.Exists(_persistencePath)) return;

                PersistedBaseline? persisted = JsonSerializer.Deserialize<PersistedBaseline>(
                    File.ReadAllText(_persistencePath));
                if (persisted is null ||
                    persisted.SchemaVersion != PersistenceSchemaVersion ||
                    persisted.Samples is null ||
                    persisted.Samples.Count > MaximumSamples)
                    return;

                PerformanceSample[] ordered = persisted.Samples
                    .Where(IsValidPersistedSample)
                    .OrderBy(sample => sample.Timestamp)
                    .ToArray();

                PerformanceSample? previous = null;
                foreach (PerformanceSample sample in ordered)
                {
                    if (previous is not null &&
                        sample.Timestamp - previous.Timestamp < MinimumSampleInterval)
                        continue;

                    _samples.Enqueue(sample);
                    previous = sample;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                // A damaged/unavailable local baseline must never prevent Sentinel from
                // monitoring. Fail closed to an empty observational history and relearn.
                _samples.Clear();
            }
        }

        private void PersistSamplesBestEffort(IReadOnlyCollection<PerformanceSample> samples)
        {
            string? directory = Path.GetDirectoryName(_persistencePath);
            string tempPath = _persistencePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                PersistedBaseline payload = new(PersistenceSchemaVersion, samples.ToArray());
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
                using (FileStream stream = new(
                           tempPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(json, 0, json.Length);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, _persistencePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Persistence improves continuity but is not itself an optimization safety
                // prerequisite. Keep the in-memory history and try again on the next sample.
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch { }
            }
        }

        private static bool IsValidPersistedSample(PerformanceSample sample) =>
            sample.Timestamp != default &&
            double.IsFinite(sample.CpuPercent) && sample.CpuPercent is >= 0 and <= 100 &&
            double.IsFinite(sample.MemoryPercent) && sample.MemoryPercent is >= 0 and <= 100 &&
            double.IsFinite(sample.DiskUsedPercent) && sample.DiskUsedPercent is >= 0 and <= 100 &&
            sample.ProcessCount >= 0 &&
            double.IsFinite(sample.DownloadMbps) && sample.DownloadMbps >= 0 &&
            double.IsFinite(sample.UploadMbps) && sample.UploadMbps >= 0;

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

        private sealed record PersistedBaseline(
            int SchemaVersion,
            IReadOnlyList<PerformanceSample> Samples);

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
