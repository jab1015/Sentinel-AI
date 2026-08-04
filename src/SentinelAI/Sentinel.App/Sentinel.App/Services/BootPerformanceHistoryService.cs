/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Reads Windows Diagnostics-Performance boot events and builds a measured
    /// startup-performance history. This service is read-only and never changes
    /// startup applications or boot configuration.
    /// </summary>
    public sealed class BootPerformanceHistoryService
    {
        private const int MaximumSamples = 10;

        public BootPerformanceHistory Assess()
        {
            const string logName = "Microsoft-Windows-Diagnostics-Performance/Operational";
            string query = "*[System[(EventID=100)]]";

            CommandResult result = Run(
                "wevtutil.exe",
                $"qe \"{logName}\" /q:\"{query}\" /f:xml /c:{MaximumSamples} /rd:true");

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            {
                return new BootPerformanceHistory(
                    Array.Empty<BootPerformanceSample>(),
                    false,
                    0,
                    0,
                    0,
                    "Sentinel could not read enough Windows boot-performance evidence. No startup change is warranted from incomplete data.",
                    result.Error);
            }

            IReadOnlyList<BootPerformanceSample> samples = ParseSamples(result.Output);
            if (samples.Count < 3)
            {
                return new BootPerformanceHistory(
                    samples,
                    false,
                    samples.Count == 0 ? 0 : samples.Average(sample => sample.BootDurationMs),
                    0,
                    0,
                    "Sentinel does not yet have enough measured boot history to make a startup-performance decision.",
                    string.Empty);
            }

            double average = samples.Average(sample => sample.BootDurationMs);
            double recentAverage = samples.Take(Math.Min(3, samples.Count))
                .Average(sample => sample.BootDurationMs);
            double olderAverage = samples.Skip(Math.Min(3, samples.Count)).Any()
                ? samples.Skip(3).Average(sample => sample.BootDurationMs)
                : average;

            double regressionPercent = olderAverage > 0
                ? ((recentAverage - olderAverage) / olderAverage) * 100d
                : 0d;

            bool sustainedRegression =
                recentAverage >= 45000d &&
                regressionPercent >= 20d;

            string summary = sustainedRegression
                ? $"Sentinel measured a sustained startup slowdown. Recent boots average {recentAverage / 1000d:0.0}s versus {olderAverage / 1000d:0.0}s previously ({regressionPercent:0}% slower)."
                : $"Measured boot history does not show a sustained startup slowdown requiring optimization. Recent boots average {recentAverage / 1000d:0.0}s.";

            return new BootPerformanceHistory(
                samples,
                sustainedRegression,
                average,
                recentAverage,
                regressionPercent,
                summary,
                string.Empty);
        }

        private static IReadOnlyList<BootPerformanceSample> ParseSamples(string xml)
        {
            var samples = new List<BootPerformanceSample>();
            string[] events = xml.Split(new[] { "</Event>" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawEvent in events)
            {
                string eventXml = rawEvent + "</Event>";
                long bootDuration = ReadDataValue(eventXml, "BootTime");
                long mainPathDuration = ReadDataValue(eventXml, "MainPathBootTime");
                long postBootDuration = ReadDataValue(eventXml, "BootPostBootTime");
                DateTimeOffset? timestamp = ReadTimestamp(eventXml);

                if (bootDuration <= 0)
                    continue;

                samples.Add(new BootPerformanceSample(
                    timestamp,
                    bootDuration,
                    mainPathDuration,
                    postBootDuration));
            }

            return samples;
        }

        private static long ReadDataValue(string xml, string name)
        {
            string marker = $"Name='{name}'>";
            int start = xml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                marker = $"Name=\"{name}\">";
                start = xml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            }

            if (start < 0)
                return 0;

            start += marker.Length;
            int end = xml.IndexOf('<', start);
            if (end <= start)
                return 0;

            string value = xml.Substring(start, end - start).Trim();
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;
        }

        private static DateTimeOffset? ReadTimestamp(string xml)
        {
            const string marker = "SystemTime='";
            int start = xml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            int quoteLength = 1;

            if (start < 0)
            {
                const string alternate = "SystemTime=\"";
                start = xml.IndexOf(alternate, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    return null;
                start += alternate.Length;
            }
            else
            {
                start += marker.Length;
            }

            int end = xml.IndexOf(quoteLength == 1 ? '\'' : '\"', start);
            if (end <= start)
                return null;

            string value = xml.Substring(start, end - start);
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed)
                ? parsed
                : null;
        }

        private static CommandResult Run(string fileName, string arguments)
        {
            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return new CommandResult(-1, output, "Boot-performance diagnostic timed out.");
                }

                return new CommandResult(process.ExitCode, output, error);
            }
            catch (Exception ex)
            {
                return new CommandResult(-1, string.Empty, ex.Message);
            }
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record BootPerformanceHistory(
        IReadOnlyList<BootPerformanceSample> Samples,
        bool SustainedRegressionDetected,
        double AverageBootDurationMs,
        double RecentAverageBootDurationMs,
        double RegressionPercent,
        string Summary,
        string DiagnosticError);

    public sealed record BootPerformanceSample(
        DateTimeOffset? Timestamp,
        long BootDurationMs,
        long MainPathBootDurationMs,
        long PostBootDurationMs);
}
