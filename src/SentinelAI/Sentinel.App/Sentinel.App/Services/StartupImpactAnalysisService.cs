/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Performs a conservative second-stage analysis of startup entries. It never
    /// disables or deletes entries. Candidates are ranked only when startup load is
    /// already elevated and Sentinel can resolve a real executable path.
    /// </summary>
    public sealed class StartupImpactAnalysisService
    {
        public StartupImpactAnalysis Analyze(StartupPerformanceAssessment assessment)
        {
            ArgumentNullException.ThrowIfNull(assessment);

            if (!assessment.DeeperAnalysisWarranted)
            {
                return new StartupImpactAnalysis(
                    Array.Empty<StartupImpactCandidate>(),
                    false,
                    "Startup load does not currently warrant deeper optimization analysis.");
            }

            var candidates = new List<StartupImpactCandidate>();

            foreach (StartupEntryEvidence entry in assessment.Entries.Where(e => !e.IsSentinel))
            {
                string? executablePath = TryResolveExecutablePath(entry.Command);
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                    continue;

                FileInfo file = new(executablePath);
                long sizeBytes = 0;
                try { sizeBytes = Math.Max(file.Length, 0); } catch { }

                StartupImpactLevel impact = sizeBytes >= 250L * 1024 * 1024
                    ? StartupImpactLevel.High
                    : sizeBytes >= 75L * 1024 * 1024
                        ? StartupImpactLevel.Moderate
                        : StartupImpactLevel.Unknown;

                // File size alone is not proof of startup cost. It is used only as
                // a weak ranking signal until runtime startup-impact telemetry exists.
                if (impact == StartupImpactLevel.Unknown)
                    continue;

                candidates.Add(new StartupImpactCandidate(
                    entry.Name,
                    entry.Scope,
                    executablePath,
                    impact,
                    "Sentinel identified this as a startup-review candidate. No change will be made until runtime impact and necessity are verified."));
            }

            bool reviewWarranted = candidates.Count > 0;
            string summary = reviewWarranted
                ? $"Sentinel identified {candidates.Count} startup entries for deeper impact verification. No startup entries have been changed."
                : "Startup entry count is elevated, but Sentinel does not yet have enough verified evidence to recommend disabling any entry.";

            return new StartupImpactAnalysis(
                candidates,
                reviewWarranted,
                summary);
        }

        private static string? TryResolveExecutablePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;

            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());

            if (expanded.StartsWith('"'))
            {
                int closingQuote = expanded.IndexOf('"', 1);
                if (closingQuote > 1)
                    return expanded.Substring(1, closingQuote - 1);
            }

            int exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
                return expanded.Substring(0, exeIndex + 4).Trim();

            return null;
        }
    }

    public sealed record StartupImpactAnalysis(
        IReadOnlyList<StartupImpactCandidate> Candidates,
        bool RuntimeImpactVerificationWarranted,
        string Summary);

    public sealed record StartupImpactCandidate(
        string Name,
        string Scope,
        string ExecutablePath,
        StartupImpactLevel Impact,
        string Reason);

    public enum StartupImpactLevel
    {
        Unknown,
        Moderate,
        High
    }
}
