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
        private static readonly TimeSpan MaximumPersistedSampleAge = TimeSpan.FromHours(24);
        private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);
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

            DateTime timestamp = NormalizeTimestamp(snapshot.Timestamp);
            PerformanceSample sample = new(
                timestamp,
                ClampFinite(snapshot.CpuUsagePercent),
                ClampFinite(snapshot.MemoryUsagePercent),
                ClampFinite(snapshot.DiskUsagePercent),
                Math.Max(snapshot.ProcessCount, 0),
                NonNegativeFinite(snapshot.DownloadMbps),
                NonNegativeFinite(snapshot.UploadMbps));

            lock (_sync)
            {
                PruneExpiredSamples(timestamp);
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
                PruneExpiredSamples(DateTime.UtcNow);
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

                DateTime nowUtc = DateTime.UtcNow;
                DateTime oldestAcceptedUtc = nowUtc - MaximumPersistedSampleAge;
                DateTime newestAcceptedUtc = nowUtc + MaximumFutureClockSkew;
                PerformanceSample[] ordered = persisted.Samples
                    .Where(sample => IsValidPersistedSample(sample, oldestAcceptedUtc, newestAcceptedUtc))
                    .Select(sample => sample with { Timestamp = sample.Timestamp.ToUniversalTime() })
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                // A damaged/unavailable local baseline must never prevent Sentinel from
                // monitoring. Fail closed to an empty observational history and relearn.
                _samples.Clear();
            }
        }

        private void PruneExpiredSamples(DateTime referenceTimestamp)
        {
            DateTime referenceUtc = NormalizeTimestamp(referenceTimestamp);
            DateTime oldestAcceptedUtc = referenceUtc - MaximumPersistedSampleAge;
            while (_samples.Count > 0 && _samples.Peek().Timestamp.ToUniversalTime() < oldestAcceptedUtc)
                _samples.Dequeue();
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

        private static bool IsValidPersistedSample(
            PerformanceSample sample,
            DateTime oldestAcceptedUtc,
            DateTime newestAcceptedUtc)
        {
            if (sample.Timestamp == default) return false;

            DateTime timestampUtc;
            try
            {
                timestampUtc = sample.Timestamp.ToUniversalTime();
            }
            catch (ArgumentException)
            {
                return false;
            }

            return timestampUtc >= oldestAcceptedUtc &&
                   timestampUtc <= newestAcceptedUtc &&
                   double.IsFinite(sample.CpuPercent) && sample.CpuPercent is >= 0 and <= 100 &&
                   double.IsFinite(sample.MemoryPercent) && sample.MemoryPercent is >= 0 and <= 100 &&
                   double.IsFinite(sample.DiskUsedPercent) && sample.DiskUsedPercent is >= 0 and <= 100 &&
                   sample.ProcessCount >= 0 &&
                   double.IsFinite(sample.DownloadMbps) && sample.DownloadMbps >= 0 &&
                   double.IsFinite(sample.UploadMbps) && sample.UploadMbps >= 0;
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
                ? $"Sentinel is learning this computer's normal performance ({samples.Count}/12 one-minute baseline samples)."
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

        private static DateTime NormalizeTimestamp(DateTime value) =>
            value == default ? DateTime.UtcNow : value.ToUniversalTime();

        private static double ClampFinite(double value) =>
            double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

        private static double NonNegativeFinite(double value) =>
            double.IsFinite(value) ? Math.Max(value, 0) : 0;

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
