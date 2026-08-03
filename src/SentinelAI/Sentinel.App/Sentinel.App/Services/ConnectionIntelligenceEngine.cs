/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts raw connection observations into evidence-weighted network intelligence.
    /// A connection is never treated as malicious merely because its port, endpoint, or
    /// process is unfamiliar. Sentinel requires corroborating local evidence before a
    /// network condition can be elevated for investigation.
    /// </summary>
    public sealed class ConnectionIntelligenceEngine
    {
        public ConnectionIntelligenceResult Analyze(SystemSnapshot snapshot)
        {
            if (!snapshot.NetworkConnectionMonitoringAvailable)
            {
                return new ConnectionIntelligenceResult(
                    ConnectionAssessmentState.Degraded,
                    0,
                    false,
                    "Network monitoring is unavailable",
                    "Sentinel could not collect current connection evidence. No threat conclusion can be made until monitoring is restored.",
                    "network-monitor-unavailable");
            }

            if (snapshot.ExternalConnectionCount <= 0 && snapshot.RecentUniqueExternalConnectionCount <= 0)
            {
                return Healthy("No external connection activity currently requires investigation.");
            }

            int score = 0;
            bool corroborated = false;
            string reasonCode = "network-observation";

            bool hasAttributedConnection = snapshot.AttributedExternalConnectionCount > 0;
            bool hasConnectionFinding = snapshot.FlaggedConnectionCount > 0 &&
                                        !IsNone(snapshot.PrimaryFlaggedConnectionProcessName);
            bool processCorrelates = hasConnectionFinding && snapshot.FlaggedProcessCount > 0 &&
                                     SameName(snapshot.PrimaryFlaggedConnectionProcessName, snapshot.PrimaryFlaggedProcessName);
            bool commandLineCorrelates = hasConnectionFinding && snapshot.FlaggedCommandLineCount > 0 &&
                                         SameName(snapshot.PrimaryFlaggedConnectionProcessName, snapshot.PrimaryCommandLineProcessName);
            bool startupCorrelates = hasConnectionFinding && snapshot.FlaggedStartupEntryCount > 0 &&
                                     ContainsEither(snapshot.PrimaryFlaggedStartupEntryName, snapshot.PrimaryFlaggedConnectionProcessName);
            bool serviceCorrelates = hasConnectionFinding && snapshot.FlaggedServiceCount > 0 &&
                                     ContainsEither(snapshot.PrimaryFlaggedServiceName, snapshot.PrimaryFlaggedConnectionProcessName);
            bool lineageCorrelates = hasConnectionFinding && snapshot.FlaggedProcessRelationshipCount > 0 &&
                                     (SameName(snapshot.PrimaryFlaggedConnectionProcessName, snapshot.PrimaryLineageChildProcessName) ||
                                      SameName(snapshot.PrimaryFlaggedConnectionProcessName, snapshot.PrimaryLineageParentProcessName));
            bool repeatedNetworkBehavior = snapshot.RepeatingExternalConnectionCount > 0;
            bool inboundExternalActivity = snapshot.InboundExternalConnectionCount > 0;

            if (hasAttributedConnection) score += 5;
            if (hasConnectionFinding) score += 10;
            if (repeatedNetworkBehavior) score += 5;
            if (inboundExternalActivity) score += 5;

            if (processCorrelates) { score += 30; corroborated = true; reasonCode = "network-process-correlation"; }
            if (commandLineCorrelates) { score += 25; corroborated = true; reasonCode = "network-commandline-correlation"; }
            if (startupCorrelates) { score += 25; corroborated = true; reasonCode = "network-persistence-correlation"; }
            if (serviceCorrelates) { score += 20; corroborated = true; reasonCode = "network-service-correlation"; }
            if (lineageCorrelates) { score += 20; corroborated = true; reasonCode = "network-lineage-correlation"; }

            score = Math.Clamp(score, 0, 100);

            if (!corroborated)
            {
                return new ConnectionIntelligenceResult(
                    ConnectionAssessmentState.Observed,
                    score,
                    false,
                    "Network activity observed",
                    "Sentinel is monitoring the connection activity, but the available evidence does not independently indicate an intrusion or spyware condition.",
                    "network-observation-only");
            }

            if (score >= 55)
            {
                string processName = IsNone(snapshot.PrimaryFlaggedConnectionProcessName)
                    ? "a running process"
                    : snapshot.PrimaryFlaggedConnectionProcessName;

                return new ConnectionIntelligenceResult(
                    ConnectionAssessmentState.Investigate,
                    score,
                    true,
                    "Correlated network activity requires investigation",
                    $"Sentinel correlated {processName} network activity with additional local warning evidence. The connection itself is not being labeled malicious; the combined evidence requires investigation.",
                    reasonCode);
            }

            return new ConnectionIntelligenceResult(
                ConnectionAssessmentState.Observed,
                score,
                true,
                "Correlated network activity is being monitored",
                "Sentinel found supporting local evidence, but the combined confidence is not yet high enough to classify the activity as requiring user attention.",
                reasonCode);
        }

        private static ConnectionIntelligenceResult Healthy(string summary) =>
            new(ConnectionAssessmentState.Healthy, 0, false, "Network activity is normal", summary, "network-healthy");

        private static bool SameName(string? left, string? right) =>
            !IsNone(left) && !IsNone(right) &&
            string.Equals(NormalizeName(left!), NormalizeName(right!), StringComparison.OrdinalIgnoreCase);

        private static bool ContainsEither(string? left, string? right)
        {
            if (IsNone(left) || IsNone(right)) return false;
            string normalizedLeft = NormalizeName(left!);
            string normalizedRight = NormalizeName(right!);
            return normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase) ||
                   normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeName(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^4];
            }
            return trimmed;
        }

        private static bool IsNone(string? value) =>
            string.IsNullOrWhiteSpace(value) ||
            value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Unknown process", StringComparison.OrdinalIgnoreCase);

        public enum ConnectionAssessmentState
        {
            Healthy,
            Observed,
            Investigate,
            Degraded
        }

        public sealed record ConnectionIntelligenceResult(
            ConnectionAssessmentState State,
            int ConfidenceScore,
            bool HasCorroboratingEvidence,
            string Title,
            string Summary,
            string ReasonCode);
    }
}
