/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Correlates independent investigation signals before escalation.
    /// A single weak signal is never sufficient to require user action.
    /// </summary>
    public sealed class MultiSignalCorrelationEngine
    {
        public CorrelationResult Correlate(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            List<string> signals = new();
            AddSignal(signals, snapshot.FlaggedProcessCount > 0, "process");
            AddSignal(signals, snapshot.FlaggedProcessRelationshipCount > 0, "process-lineage");
            AddSignal(signals, snapshot.FlaggedCommandLineCount > 0, "command-line");
            AddSignal(signals, snapshot.FlaggedStartupEntryCount > 0, "startup-persistence");
            AddSignal(signals, snapshot.FlaggedScheduledTaskCount > 0, "scheduled-task");
            AddSignal(signals, snapshot.FlaggedConnectionCount > 0, "network");
            AddSignal(signals, snapshot.FlaggedServiceCount > 0, "service");
            AddSignal(signals, snapshot.CriticalEventCount > 0 || snapshot.ErrorEventCount > 0, "event-log");
            AddSignal(signals, !snapshot.DefenderEnabled || !snapshot.FirewallEnabled, "security-control");

            int confidence = CalculateConfidence(signals.Count, snapshot);
            bool requiresAttention =
                signals.Contains("security-control") ||
                confidence >= 70;

            string summary = signals.Count switch
            {
                0 => "No correlated investigation signals were detected.",
                1 => $"One investigation signal is under review: {signals[0]}.",
                _ => $"Sentinel correlated {signals.Count} independent investigation signals: {string.Join(", ", signals)}."
            };

            return new CorrelationResult(
                signals.Count,
                confidence,
                requiresAttention,
                summary,
                signals.ToArray());
        }

        private static int CalculateConfidence(int signalCount, SystemSnapshot snapshot)
        {
            int confidence = signalCount switch
            {
                <= 0 => 0,
                1 => 25,
                2 => 55,
                3 => 75,
                _ => 90
            };

            if (!snapshot.DefenderEnabled || !snapshot.FirewallEnabled)
            {
                confidence = Math.Max(confidence, 95);
            }

            if (snapshot.RiskScore >= 50)
            {
                confidence = Math.Min(100, confidence + 10);
            }

            return confidence;
        }

        private static void AddSignal(List<string> signals, bool condition, string signal)
        {
            if (condition)
            {
                signals.Add(signal);
            }
        }

        public sealed record CorrelationResult(
            int SignalCount,
            int ConfidencePercent,
            bool RequiresAttention,
            string Summary,
            IReadOnlyList<string> Signals);
    }
}
