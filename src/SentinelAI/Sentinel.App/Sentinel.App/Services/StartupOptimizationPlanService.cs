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
    /// Correlates startup entries with currently running processes and produces a
    /// conservative optimization plan. This service is read-only and never disables
    /// startup entries on its own.
    /// </summary>
    public sealed class StartupOptimizationPlanService
    {
        private readonly StartupPerformanceAssessmentService _assessmentService = new();

        public StartupOptimizationPlan BuildPlan()
        {
            StartupPerformanceAssessment assessment = _assessmentService.Assess();

            if (!assessment.DeeperAnalysisWarranted)
            {
                return StartupOptimizationPlan.NoAction(assessment, assessment.Summary);
            }

            var processes = Process.GetProcesses();
            var candidates = new List<StartupOptimizationCandidate>();

            foreach (StartupEntryEvidence entry in assessment.Entries.Where(e => !e.IsSentinel))
            {
                string normalized = NormalizeCommand(entry.Command);
                string executableName = TryGetExecutableName(normalized);
                if (string.IsNullOrWhiteSpace(executableName))
                    continue;

                Process? process = processes.FirstOrDefault(p =>
                {
                    try
                    {
                        return p.ProcessName.Equals(executableName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (process is null)
                    continue;

                long workingSet = 0;
                try { workingSet = Math.Max(process.WorkingSet64, 0); } catch { }

                bool meaningfulRuntimeCost = workingSet >= 150L * 1024 * 1024;
                if (!meaningfulRuntimeCost)
                    continue;

                candidates.Add(new StartupOptimizationCandidate(
                    entry.Name,
                    entry.Command,
                    entry.Scope,
                    executableName,
                    workingSet,
                    "This startup item is currently running and is using a meaningful amount of memory. Sentinel should verify boot impact and user need before recommending a startup change.",
                    AutomaticDisableAllowed: false));
            }

            if (candidates.Count == 0)
            {
                return StartupOptimizationPlan.NoAction(
                    assessment,
                    "Startup entry count is elevated, but Sentinel did not verify a currently running startup item with enough impact to justify a change.");
            }

            return new StartupOptimizationPlan(
                assessment,
                true,
                candidates
                    .OrderByDescending(candidate => candidate.WorkingSetBytes)
                    .ToArray(),
                "Sentinel identified startup items with measurable runtime cost. They require user-need and boot-impact verification before any startup change is allowed.");
        }

        private static string NormalizeCommand(string command) =>
            Environment.ExpandEnvironmentVariables(command ?? string.Empty).Trim();

        private static string TryGetExecutableName(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return string.Empty;

            try
            {
                string token;
                if (command.StartsWith('"'))
                {
                    int closingQuote = command.IndexOf('"', 1);
                    token = closingQuote > 1 ? command.Substring(1, closingQuote - 1) : command.Trim('"');
                }
                else
                {
                    int firstSpace = command.IndexOf(' ');
                    token = firstSpace > 0 ? command[..firstSpace] : command;
                }

                string fileName = System.IO.Path.GetFileNameWithoutExtension(token);
                return fileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public sealed record StartupOptimizationPlan(
        StartupPerformanceAssessment Assessment,
        bool ActionWarranted,
        IReadOnlyList<StartupOptimizationCandidate> Candidates,
        string Summary)
    {
        public static StartupOptimizationPlan NoAction(
            StartupPerformanceAssessment assessment,
            string summary) =>
            new(assessment, false, Array.Empty<StartupOptimizationCandidate>(), summary);
    }

    public sealed record StartupOptimizationCandidate(
        string Name,
        string Command,
        string Scope,
        string ProcessName,
        long WorkingSetBytes,
        string Evidence,
        bool AutomaticDisableAllowed);
}
