/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Correlates independent process, persistence, lineage, command-line and network
    /// observations. No single unfamiliar process or connection is sufficient for a
    /// spyware conclusion.
    /// </summary>
    public sealed class SpywareCorrelationEngine
    {
        public SpywareCorrelationResult Analyze(SystemSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            bool processEvidence = snapshot.FlaggedProcessCount > 0;
            bool commandEvidence = snapshot.FlaggedCommandLineCount > 0;
            bool lineageEvidence = snapshot.FlaggedProcessRelationshipCount > 0;
            bool startupEvidence = snapshot.FlaggedStartupEntryCount > 0;
            bool taskEvidence = snapshot.FlaggedScheduledTaskCount > 0;
            bool serviceEvidence = snapshot.FlaggedServiceCount > 0;
            bool networkEvidence = snapshot.ConnectionIntelligenceHasCorroboratingEvidence &&
                                   snapshot.ConnectionIntelligenceConfidenceScore >= 55;

            int evidenceFamilies = Count(processEvidence, commandEvidence, lineageEvidence,
                startupEvidence, taskEvidence, serviceEvidence, networkEvidence);

            bool sameProcessNetworkCorrelation = networkEvidence &&
                NamesMatch(snapshot.PrimaryFlaggedConnectionProcessName, snapshot.PrimaryFlaggedProcessName,
                    snapshot.PrimaryCommandLineProcessName, snapshot.PrimaryLineageChildProcessName);

            bool persistenceCorrelation = (startupEvidence || taskEvidence || serviceEvidence) &&
                                          (processEvidence || commandEvidence || lineageEvidence || networkEvidence);

            int confidence = Math.Min(95,
                (evidenceFamilies * 14) +
                (sameProcessNetworkCorrelation ? 18 : 0) +
                (persistenceCorrelation ? 16 : 0));

            bool monitoringIncomplete =
                !snapshot.NetworkConnectionMonitoringAvailable ||
                !snapshot.CommandLineMonitoringAvailable ||
                !snapshot.ProcessLineageMonitoringAvailable ||
                !snapshot.StartupPersistenceMonitoringAvailable ||
                !snapshot.ScheduledTaskMonitoringAvailable;

            if (evidenceFamilies < 2 && monitoringIncomplete)
            {
                return new SpywareCorrelationResult(
                    SpywareCorrelationState.EvidenceIncomplete,
                    confidence,
                    false,
                    "Spyware correlation evidence is incomplete",
                    "Sentinel could not collect all current network and persistence evidence. No spyware conclusion can be made until monitoring coverage is restored.",
                    "spyware-evidence-incomplete");
            }

            if (evidenceFamilies < 2)
            {
                return new SpywareCorrelationResult(
                    SpywareCorrelationState.NoCorroboratedConcern,
                    confidence,
                    false,
                    "No corroborated spyware behavior detected",
                    "Sentinel did not find enough independent evidence to classify current activity as spyware-like behavior.",
                    "spyware-no-corroboration");
            }

            if (sameProcessNetworkCorrelation && persistenceCorrelation && confidence >= 70)
            {
                return new SpywareCorrelationResult(
                    SpywareCorrelationState.HighConcern,
                    confidence,
                    true,
                    "Correlated spyware-like behavior requires investigation",
                    "Sentinel correlated suspicious process/network behavior with persistence or execution evidence. This is a high-confidence behavioral concern, not a claim that a specific malware family has been identified.",
                    "spyware-correlated-high");
            }

            if (persistenceCorrelation || sameProcessNetworkCorrelation || evidenceFamilies >= 3)
            {
                return new SpywareCorrelationResult(
                    SpywareCorrelationState.Review,
                    confidence,
                    true,
                    "Correlated behavior requires review",
                    "Multiple independent observations overlap. Sentinel should investigate the responsible process and persistence/network evidence before taking containment action.",
                    "spyware-correlated-review");
            }

            return new SpywareCorrelationResult(
                SpywareCorrelationState.Observe,
                confidence,
                false,
                "Additional evidence is being collected",
                "Sentinel observed more than one unusual condition, but the evidence is not sufficiently correlated for a spyware concern.",
                "spyware-observe");
        }

        private static int Count(params bool[] values)
        {
            int count = 0;
            foreach (bool value in values) if (value) count++;
            return count;
        }

        private static bool NamesMatch(string networkProcess, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(networkProcess) || networkProcess.Equals("None", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || candidate.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                if (networkProcess.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public enum SpywareCorrelationState
        {
            EvidenceIncomplete,
            NoCorroboratedConcern,
            Observe,
            Review,
            HighConcern
        }

        public sealed record SpywareCorrelationResult(
            SpywareCorrelationState State,
            int ConfidenceScore,
            bool HasCorroboratingEvidence,
            string Title,
            string Summary,
            string ReasonCode);
    }
}
