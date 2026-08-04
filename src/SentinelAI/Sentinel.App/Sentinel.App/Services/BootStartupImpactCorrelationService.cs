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
    /// Correlates sustained boot regression with Windows Diagnostics-Performance
    /// startup degradation events. This service is read-only and does not disable
    /// startup software.
    /// </summary>
    public sealed class BootStartupImpactCorrelationService
    {
        private const int MaximumEvents = 40;
        private readonly BootPerformanceHistoryService _bootHistoryService = new();
        private readonly StartupOptimizationPlanService _startupPlanService = new();

        public BootStartupImpactCorrelation Assess()
        {
            BootPerformanceHistory history = _bootHistoryService.Assess();
            StartupOptimizationPlan startupPlan = _startupPlanService.BuildPlan();

            if (!history.SustainedRegressionDetected)
            {
                return BootStartupImpactCorrelation.NoAction(
                    history,
                    startupPlan,
                    "Measured boot history does not show a sustained regression, so Sentinel will not consider disabling startup software.");
            }

            if (!startupPlan.ActionWarranted || startupPlan.Candidates.Count == 0)
            {
                return BootStartupImpactCorrelation.NoAction(
                    history,
                    startupPlan,
                    "Boot slowdown is verified, but Sentinel did not identify a startup candidate with measurable runtime cost.");
            }

            const string logName = "Microsoft-Windows-Diagnostics-Performance/Operational";
            const string query = "*[System[(EventID=101 or EventID=102 or EventID=103 or EventID=106 or EventID=107 or EventID=108 or EventID=109 or EventID=110)]]";

            CommandResult result = Run(
                "wevtutil.exe",
                $"qe \"{logName}\" /q:\"{query}\" /f:xml /c:{MaximumEvents} /rd:true");

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            {
                return BootStartupImpactCorrelation.NoAction(
                    history,
                    startupPlan,
                    "Sentinel verified boot slowdown but could not read Windows startup-degradation evidence. No startup change is allowed.",
                    result.Error);
            }

            IReadOnlyList<StartupDegradationEvidence> degradation = ParseEvents(result.Output);
            var correlated = new List<CorrelatedStartupImpact>();

            foreach (StartupOptimizationCandidate candidate in startupPlan.Candidates)
            {
                StartupDegradationEvidence[] matches = degradation
                    .Where(item => MatchesCandidate(item, candidate))
                    .ToArray();

                if (matches.Length == 0)
                    continue;

                long totalDegradationMs = matches.Sum(item => Math.Max(item.DegradationTimeMs, 0));
                long maximumDegradationMs = matches.Max(item => Math.Max(item.DegradationTimeMs, 0));

                // Repeated degradation evidence is intentionally required. One event
                // can be caused by updates, first-run initialization, or transient I/O.
                bool repeatedImpact = matches.Length >= 2 && maximumDegradationMs >= 1000;

                correlated.Add(new CorrelatedStartupImpact(
                    candidate,
                    matches,
                    totalDegradationMs,
                    maximumDegradationMs,
                    repeatedImpact,
                    repeatedImpact
                        ? "Windows repeatedly attributed measurable startup degradation to this startup item."
                        : "Windows recorded startup degradation for this item, but the evidence is not yet repeated strongly enough for an automatic change."));
            }

            CorrelatedStartupImpact[] verified = correlated
                .Where(item => item.RepeatedImpactVerified)
                .OrderByDescending(item => item.MaximumDegradationTimeMs)
                .ToArray();

            string summary = verified.Length > 0
                ? $"Sentinel correlated sustained boot slowdown with repeated Windows degradation evidence for {verified.Length} startup item(s). A final safety decision is now possible."
                : "Sentinel verified boot slowdown but could not attribute it repeatedly to a specific startup item. No startup change is warranted.";

            return new BootStartupImpactCorrelation(
                history,
                startupPlan,
                correlated,
                verified,
                verified.Length > 0,
                summary,
                result.Error);
        }

        private static bool MatchesCandidate(
            StartupDegradationEvidence evidence,
            StartupOptimizationCandidate candidate)
        {
            string process = candidate.ProcessName ?? string.Empty;
            string name = candidate.Name ?? string.Empty;

            return (!string.IsNullOrWhiteSpace(process) &&
                    (evidence.FileName.Contains(process, StringComparison.OrdinalIgnoreCase) ||
                     evidence.FriendlyName.Contains(process, StringComparison.OrdinalIgnoreCase))) ||
                   (!string.IsNullOrWhiteSpace(name) &&
                    evidence.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<StartupDegradationEvidence> ParseEvents(string xml)
        {
            var events = new List<StartupDegradationEvidence>();
            string[] blocks = xml.Split(new[] { "</Event>" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string block in blocks)
            {
                string eventXml = block + "</Event>";
                int eventId = (int)ReadLong(eventXml, "EventID");
                string friendlyName = ReadData(eventXml, "FriendlyName");
                string fileName = ReadData(eventXml, "FileName");
                long totalTime = ReadLong(eventXml, "TotalTime");
                long degradationTime = ReadLong(eventXml, "DegradationTime");

                if (string.IsNullOrWhiteSpace(friendlyName) && string.IsNullOrWhiteSpace(fileName))
                    continue;

                events.Add(new StartupDegradationEvidence(
                    eventId,
                    friendlyName,
                    fileName,
                    totalTime,
                    degradationTime));
            }

            return events;
        }

        private static string ReadData(string xml, string name)
        {
            string[] markers = { $"Name='{name}'>", $"Name=\"{name}\">" };
            foreach (string marker in markers)
            {
                int start = xml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    continue;

                start += marker.Length;
                int end = xml.IndexOf('<', start);
                if (end > start)
                    return xml.Substring(start, end - start).Trim();
            }

            if (name.Equals("EventID", StringComparison.OrdinalIgnoreCase))
            {
                const string open = "<EventID>";
                int start = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    start += open.Length;
                    int end = xml.IndexOf('<', start);
                    if (end > start)
                        return xml.Substring(start, end - start).Trim();
                }
            }

            return string.Empty;
        }

        private static long ReadLong(string xml, string name)
        {
            string value = ReadData(xml, name);
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;
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
                    return new CommandResult(-1, output, "Startup-impact diagnostic timed out.");
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

    public sealed record BootStartupImpactCorrelation(
        BootPerformanceHistory BootHistory,
        StartupOptimizationPlan StartupPlan,
        IReadOnlyList<CorrelatedStartupImpact> CorrelatedItems,
        IReadOnlyList<CorrelatedStartupImpact> VerifiedItems,
        bool VerifiedStartupCauseFound,
        string Summary,
        string DiagnosticError)
    {
        public static BootStartupImpactCorrelation NoAction(
            BootPerformanceHistory history,
            StartupOptimizationPlan startupPlan,
            string summary,
            string diagnosticError = "") =>
            new(
                history,
                startupPlan,
                Array.Empty<CorrelatedStartupImpact>(),
                Array.Empty<CorrelatedStartupImpact>(),
                false,
                summary,
                diagnosticError);
    }

    public sealed record CorrelatedStartupImpact(
        StartupOptimizationCandidate Candidate,
        IReadOnlyList<StartupDegradationEvidence> Evidence,
        long TotalDegradationTimeMs,
        long MaximumDegradationTimeMs,
        bool RepeatedImpactVerified,
        string Summary);

    public sealed record StartupDegradationEvidence(
        int EventId,
        string FriendlyName,
        string FileName,
        long TotalTimeMs,
        long DegradationTimeMs);
}
