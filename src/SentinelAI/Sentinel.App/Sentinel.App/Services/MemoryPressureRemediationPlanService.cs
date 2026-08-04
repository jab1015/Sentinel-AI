/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts persistent memory-pressure evidence into conservative remediation
    /// candidates. Sentinel never treats high memory use alone as permission to
    /// terminate an application.
    /// </summary>
    public sealed class MemoryPressureRemediationPlanService
    {
        private readonly MemoryPressureHistoryService _historyService = new();

        public MemoryPressureRemediationPlan BuildPlan()
        {
            MemoryPressureHistoryAssessment history = _historyService.RecordAndAssess();

            if (!history.PersistentPressureVerified)
            {
                return MemoryPressureRemediationPlan.NoAction(history, history.Summary);
            }

            MemoryProcessHistoryEvidence? candidate = history.PersistentProcessEvidence
                .Where(item => !item.IsSentinel && !item.IsSystemLike)
                .OrderByDescending(item => item.HighMemorySampleCount)
                .ThenByDescending(item => item.PeakWorkingSetBytes)
                .FirstOrDefault();

            if (candidate is null)
            {
                return MemoryPressureRemediationPlan.NoAction(
                    history,
                    "Sentinel verified persistent memory pressure but did not identify a safe user-process candidate. No automatic process action is warranted.");
            }

            var candidates = new List<MemoryPressureRemediationCandidate>
            {
                new(
                    MemoryPressureRemediationAction.RequestUserApplicationClose,
                    candidate.ProcessId,
                    candidate.ProcessName,
                    "Recommend closing a persistent high-memory application",
                    $"{candidate.ProcessName} repeatedly used substantial memory during verified system pressure. Closing the application normally is the safest remediation.",
                    AutomaticEligible: false,
                    Destructive: false),

                new(
                    MemoryPressureRemediationAction.TerminateProcess,
                    candidate.ProcessId,
                    candidate.ProcessName,
                    "Force-stop a persistent high-memory application",
                    "Force termination can cause unsaved-data loss and is never eligible for silent automatic optimization.",
                    AutomaticEligible: false,
                    Destructive: true)
            };

            return new MemoryPressureRemediationPlan(
                history,
                true,
                candidates,
                "Sentinel verified persistent memory pressure and a repeated high-memory user process. Safe remediation is to ask the user to close the application normally; automatic termination remains blocked.");
        }
    }

    public sealed record MemoryPressureRemediationPlan(
        MemoryPressureHistoryAssessment History,
        bool ActionWarranted,
        IReadOnlyList<MemoryPressureRemediationCandidate> Candidates,
        string Summary)
    {
        public static MemoryPressureRemediationPlan NoAction(
            MemoryPressureHistoryAssessment history,
            string summary) =>
            new(history, false, Array.Empty<MemoryPressureRemediationCandidate>(), summary);
    }

    public sealed record MemoryPressureRemediationCandidate(
        MemoryPressureRemediationAction Action,
        int ProcessId,
        string ProcessName,
        string Title,
        string Evidence,
        bool AutomaticEligible,
        bool Destructive);

    public enum MemoryPressureRemediationAction
    {
        None,
        RequestUserApplicationClose,
        TerminateProcess
    }
}
