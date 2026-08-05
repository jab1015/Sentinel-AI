/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Correlates multiple verified observations into a smaller set of candidate
    /// investigations. Correlation is evidence-preserving: Sentinel never treats
    /// a weak relationship as a verified root cause and never suppresses an
    /// independent critical/security-control finding merely because another
    /// observation appears related.
    /// </summary>
    public sealed class CrossInvestigationCorrelationEngine
    {
        public CorrelationAssessment Analyze(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var observations = CollectObservations(snapshot);
            if (observations.Count == 0)
                return CorrelationAssessment.Healthy;

            var groups = new List<CorrelationGroup>();

            CorrelateProcessBehavior(observations, groups);
            CorrelateSecurityControls(observations, groups);
            CorrelateServiceAndEventEvidence(observations, groups);
            CorrelateDriverEvidence(observations, groups);

            var assigned = new HashSet<string>(
                groups.SelectMany(group => group.ObservationIds),
                StringComparer.OrdinalIgnoreCase);

            foreach (Observation observation in observations.Where(item => !assigned.Contains(item.Id)))
            {
                groups.Add(new CorrelationGroup(
                    CorrelationId: $"standalone:{observation.Id}",
                    RootCauseCandidate: observation.Title,
                    ConfidencePercent: observation.ConfidencePercent,
                    IsVerifiedRootCause: observation.Verified,
                    RequiresAttention: observation.RequiresAttention,
                    Severity: observation.Severity,
                    Summary: observation.Summary,
                    ObservationIds: new[] { observation.Id }));
            }

            CorrelationGroup primary = groups
                .OrderByDescending(group => SeverityRank(group.Severity))
                .ThenByDescending(group => group.RequiresAttention)
                .ThenByDescending(group => group.ConfidencePercent)
                .ThenByDescending(group => group.ObservationIds.Count)
                .First();

            int correlatedObservationCount = groups
                .Where(group => group.ObservationIds.Count > 1)
                .Sum(group => group.ObservationIds.Count);

            return new CorrelationAssessment(
                observations.Count,
                groups.Count,
                correlatedObservationCount,
                primary.CorrelationId,
                primary.RootCauseCandidate,
                primary.ConfidencePercent,
                primary.IsVerifiedRootCause,
                primary.RequiresAttention,
                primary.Severity,
                BuildUserSummary(observations.Count, groups.Count, primary),
                groups);
        }

        private static List<Observation> CollectObservations(SystemSnapshot snapshot)
        {
            var observations = new List<Observation>();

            Add(observations,
                snapshot.FlaggedProcessCount > 0,
                "process",
                string.IsNullOrWhiteSpace(snapshot.PrimaryFlaggedProcessName) ? "Suspicious process behavior" : snapshot.PrimaryFlaggedProcessName,
                snapshot.PrimaryFlaggedProcessReason,
                70,
                false,
                true,
                "Attention");

            Add(observations,
                snapshot.FlaggedProcessRelationshipCount > 0,
                "process-lineage",
                "Unusual process relationship",
                $"{snapshot.PrimaryLineageParentProcessName} → {snapshot.PrimaryLineageChildProcessName}: {snapshot.PrimaryLineageReason}",
                75,
                false,
                true,
                "Attention");

            Add(observations,
                snapshot.FlaggedCommandLineCount > 0,
                "command-line",
                "Suspicious command-line behavior",
                snapshot.PrimaryCommandLineReason,
                70,
                false,
                true,
                "Attention");

            Add(observations,
                snapshot.FlaggedStartupEntryCount > 0,
                "startup-persistence",
                "Startup persistence change",
                snapshot.PrimaryFlaggedStartupEntryReason,
                70,
                false,
                true,
                "Attention");

            Add(observations,
                snapshot.FlaggedScheduledTaskCount > 0,
                "scheduled-task",
                "Scheduled-task persistence",
                snapshot.PrimaryFlaggedScheduledTaskReason,
                70,
                false,
                true,
                "Attention");

            Add(observations,
                snapshot.FlaggedConnectionCount > 0,
                "network",
                "Suspicious network behavior",
                snapshot.PrimaryFlaggedConnectionReason,
                Math.Max(70, snapshot.ConnectionIntelligenceConfidenceScore),
                snapshot.ConnectionIntelligenceHasCorroboratingEvidence,
                true,
                "Attention");

            Add(observations,
                snapshot.FlaggedServiceCount > 0,
                "service",
                string.IsNullOrWhiteSpace(snapshot.PrimaryFlaggedServiceName) ? "Service condition" : snapshot.PrimaryFlaggedServiceName,
                snapshot.PrimaryFlaggedServiceReason,
                80,
                true,
                true,
                "Attention");

            Add(observations,
                snapshot.CriticalEventCount > 0 || snapshot.ErrorEventCount > 0,
                "event-log",
                string.IsNullOrWhiteSpace(snapshot.LatestEventSource) ? "Windows event failure" : snapshot.LatestEventSource,
                snapshot.LatestEventMessage,
                snapshot.CriticalEventCount > 0 ? 95 : 80,
                true,
                true,
                snapshot.CriticalEventCount > 0 ? "Critical" : "Attention");

            Add(observations,
                !snapshot.DefenderEnabled,
                "defender-disabled",
                "Microsoft Defender is disabled",
                "Windows reports Microsoft Defender protection as disabled.",
                100,
                true,
                true,
                "Critical");

            Add(observations,
                !snapshot.FirewallEnabled,
                "firewall-disabled",
                "Windows Firewall is disabled",
                "Windows reports Firewall protection as disabled.",
                100,
                true,
                true,
                "Critical");

            Add(observations,
                snapshot.InvestigationReasonCode?.StartsWith("driver:", StringComparison.OrdinalIgnoreCase) == true,
                "driver",
                string.IsNullOrWhiteSpace(snapshot.GuidanceTitle) ? "Driver condition" : snapshot.GuidanceTitle,
                string.IsNullOrWhiteSpace(snapshot.InvestigationSummary) ? snapshot.GuidanceWhatHappened : snapshot.InvestigationSummary,
                Math.Max(80, snapshot.GuidanceConfidencePercent),
                snapshot.GuidanceConfidencePercent >= 95,
                snapshot.InvestigationRequiresAttention,
                snapshot.GuidanceSeverity);

            return observations;
        }

        private static void CorrelateProcessBehavior(IReadOnlyList<Observation> observations, ICollection<CorrelationGroup> groups)
        {
            string[] ids = { "process", "process-lineage", "command-line", "startup-persistence", "scheduled-task", "network" };
            var matches = observations.Where(item => ids.Contains(item.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (matches.Count < 2)
                return;

            int confidence = Math.Min(95, 45 + (matches.Count * 10));
            bool corroborated = matches.Any(item => item.Id == "network") &&
                                matches.Any(item => item.Id is "process" or "process-lineage" or "command-line");
            if (corroborated)
                confidence = Math.Max(confidence, 85);

            groups.Add(new CorrelationGroup(
                "correlation:process-behavior",
                "Correlated process behavior",
                confidence,
                IsVerifiedRootCause: false,
                RequiresAttention: confidence >= 70,
                Severity: confidence >= 85 ? "High" : "Attention",
                Summary: $"Sentinel correlated {matches.Count} process, persistence, command-line, or network observations. These signals are related enough to investigate together, but the root cause is not yet verified.",
                matches.Select(item => item.Id).ToArray()));
        }

        private static void CorrelateSecurityControls(IReadOnlyList<Observation> observations, ICollection<CorrelationGroup> groups)
        {
            var matches = observations.Where(item => item.Id is "defender-disabled" or "firewall-disabled").ToList();
            if (matches.Count == 0)
                return;

            groups.Add(new CorrelationGroup(
                "correlation:security-controls",
                matches.Count == 2 ? "Windows security protections are disabled" : matches[0].Title,
                100,
                IsVerifiedRootCause: true,
                RequiresAttention: true,
                Severity: "Critical",
                Summary: matches.Count == 2
                    ? "Windows independently verifies that both Microsoft Defender and Firewall are disabled. Sentinel keeps this as a critical security investigation and will not suppress it."
                    : matches[0].Summary,
                matches.Select(item => item.Id).ToArray()));
        }

        private static void CorrelateServiceAndEventEvidence(IReadOnlyList<Observation> observations, ICollection<CorrelationGroup> groups)
        {
            Observation? service = observations.FirstOrDefault(item => item.Id == "service");
            Observation? eventLog = observations.FirstOrDefault(item => item.Id == "event-log");
            if (service is null || eventLog is null)
                return;

            bool textualLink = SharesMeaningfulText(service.Title, eventLog.Summary) ||
                               SharesMeaningfulText(eventLog.Title, service.Summary);
            if (!textualLink)
                return;

            groups.Add(new CorrelationGroup(
                "correlation:service-event",
                service.Title,
                92,
                IsVerifiedRootCause: true,
                RequiresAttention: true,
                Severity: eventLog.Severity == "Critical" ? "Critical" : "Attention",
                Summary: "Sentinel correlated a verified service condition with matching Windows event evidence. These observations are presented as one investigation rather than duplicate warnings.",
                new[] { service.Id, eventLog.Id }));
        }

        private static void CorrelateDriverEvidence(IReadOnlyList<Observation> observations, ICollection<CorrelationGroup> groups)
        {
            Observation? driver = observations.FirstOrDefault(item => item.Id == "driver");
            if (driver is null)
                return;

            var related = new List<Observation> { driver };
            Observation? eventLog = observations.FirstOrDefault(item => item.Id == "event-log");
            if (eventLog is not null && SharesMeaningfulText(driver.Summary, eventLog.Summary))
                related.Add(eventLog);

            groups.Add(new CorrelationGroup(
                "correlation:driver",
                driver.Title,
                related.Count > 1 ? 98 : driver.ConfidencePercent,
                IsVerifiedRootCause: driver.Verified,
                RequiresAttention: driver.RequiresAttention,
                Severity: driver.Severity,
                Summary: related.Count > 1
                    ? "Sentinel correlated the verified driver condition with matching Windows event evidence and will treat them as one investigation."
                    : driver.Summary,
                related.Select(item => item.Id).ToArray()));
        }

        private static bool SharesMeaningfulText(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            string[] tokens = left.Split(new[] { ' ', '(', ')', ':', '-', '_', '.', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 5)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return tokens.Any(token => right.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildUserSummary(int observationCount, int investigationCount, CorrelationGroup primary)
        {
            if (observationCount == investigationCount)
                return $"Sentinel found {investigationCount} independent investigation{(investigationCount == 1 ? string.Empty : "s")}. No unsupported causal relationship was assumed.";

            return $"Sentinel grouped {observationCount} observations into {investigationCount} investigation{(investigationCount == 1 ? string.Empty : "s")}. The primary investigation is {primary.RootCauseCandidate} ({primary.ConfidencePercent}% correlation confidence).";
        }

        private static int SeverityRank(string? severity) => severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "attention" => 2,
            "medium" => 1,
            _ => 0
        };

        private static void Add(
            ICollection<Observation> observations,
            bool condition,
            string id,
            string title,
            string? summary,
            int confidencePercent,
            bool verified,
            bool requiresAttention,
            string? severity)
        {
            if (!condition)
                return;

            observations.Add(new Observation(
                id,
                title,
                string.IsNullOrWhiteSpace(summary) ? "Verified observation available." : summary.Trim(),
                Math.Clamp(confidencePercent, 0, 100),
                verified,
                requiresAttention,
                string.IsNullOrWhiteSpace(severity) ? "Attention" : severity.Trim()));
        }

        private sealed record Observation(
            string Id,
            string Title,
            string Summary,
            int ConfidencePercent,
            bool Verified,
            bool RequiresAttention,
            string Severity);

        public sealed record CorrelationGroup(
            string CorrelationId,
            string RootCauseCandidate,
            int ConfidencePercent,
            bool IsVerifiedRootCause,
            bool RequiresAttention,
            string Severity,
            string Summary,
            IReadOnlyList<string> ObservationIds);

        public sealed record CorrelationAssessment(
            int ObservationCount,
            int InvestigationCount,
            int CorrelatedObservationCount,
            string PrimaryCorrelationId,
            string PrimaryRootCauseCandidate,
            int PrimaryConfidencePercent,
            bool PrimaryRootCauseVerified,
            bool RequiresAttention,
            string Severity,
            string Summary,
            IReadOnlyList<CorrelationGroup> Groups)
        {
            public static CorrelationAssessment Healthy { get; } = new(
                0, 0, 0, string.Empty, string.Empty, 0, false, false, "Healthy",
                "No investigation observations require correlation.",
                Array.Empty<CorrelationGroup>());
        }
    }
}
