/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class AiEvidencePackageBuilder
    {
        private const int DefaultCharacterBudget = 5_000;

        public AiEvidencePackage Build(
            string purpose,
            string userQuestion,
            SystemSnapshot snapshot,
            ExternalInvestigationResult? external = null,
            int characterBudget = DefaultCharacterBudget,
            string? supplementalEvidence = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            ArgumentNullException.ThrowIfNull(snapshot);
            characterBudget = Math.Clamp(characterBudget, 1_500, 8_000);

            List<string> facts = new();
            Add(facts, "snapshot-time", snapshot.Timestamp.ToString("O"));
            Add(facts, "protection-state", $"Defender={snapshot.DefenderStatus}; Firewall={snapshot.FirewallStatus}; {snapshot.ProtectionHealthSummary}");
            Add(facts, "monitoring-coverage",
                $"network={Availability(snapshot.NetworkConnectionMonitoringAvailable)}; event-log={Availability(snapshot.EventLogMonitoringAvailable)}; authentication={Availability(snapshot.AuthenticationMonitoringAvailable)}; process={Availability(snapshot.ProcessMonitoringAvailable)}; command-line={Availability(snapshot.CommandLineMonitoringAvailable)}; lineage={Availability(snapshot.ProcessLineageMonitoringAvailable)}; service={Availability(snapshot.ServiceMonitoringAvailable)}; startup={Availability(snapshot.StartupPersistenceMonitoringAvailable)}; scheduled-task={Availability(snapshot.ScheduledTaskMonitoringAvailable)}; crash={Availability(snapshot.CrashEvidenceAvailable)}");
            Add(facts, "network-counts", $"external={snapshot.ExternalConnectionCount}; inbound={snapshot.InboundExternalConnectionCount}; outbound={snapshot.OutboundExternalConnectionCount}; listening-tcp={snapshot.ListeningTcpEndpointCount}; attributed={snapshot.AttributedExternalConnectionCount}");
            Add(facts, "authentication", snapshot.AuthenticationAnomalySummary);
            Add(facts, "reason", snapshot.InvestigationReasonCode);
            Add(facts, "conclusion", snapshot.InvestigationConclusion);
            Add(facts, "summary", snapshot.InvestigationSummary);
            Add(facts, "guidance", snapshot.GuidanceEvidence);

            if (snapshot.FlaggedProcessCount > 0) Add(facts, "process", $"{snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}");
            if (snapshot.FlaggedConnectionCount > 0) Add(facts, "network", $"{snapshot.PrimaryFlaggedConnectionProcessName} -> {RedactEndpoint(snapshot.PrimaryFlaggedConnectionRemoteEndpoint)}: {snapshot.PrimaryFlaggedConnectionReason}");
            if (snapshot.FlaggedServiceCount > 0) Add(facts, "service", snapshot.PrimaryFlaggedServiceName);
            if (snapshot.FlaggedCommandLineCount > 0) Add(facts, "command", $"{snapshot.PrimaryCommandLineProcessName}: {snapshot.PrimaryCommandLineReason}");
            if (snapshot.FlaggedStartupEntryCount > 0) Add(facts, "startup", $"{snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}");
            if (snapshot.FlaggedScheduledTaskCount > 0) Add(facts, "task", $"{snapshot.PrimaryFlaggedScheduledTaskName}: {snapshot.PrimaryFlaggedScheduledTaskReason}");
            Add(facts, "windows-crash", snapshot.RecentCrashSummary);

            if (external is not null)
            {
                Add(facts, "external-topic", external.Topic);
                Add(facts, "external-summary", external.Summary);
                if (external.MatchedTerms.Count > 0) Add(facts, "external-matches", string.Join(", ", external.MatchedTerms.Take(10)));
                if (external.Sources.Count > 0) Add(facts, "authorities", string.Join(", ", external.Sources.Where(x => x.Reached).Select(x => x.SourceName).Distinct().Take(5)));
            }

            Add(facts, "machine-specific supplemental evidence", supplementalEvidence);

            bool redactionApplied = false;
            string sanitizedQuestion = Sanitize(userQuestion, ref redactionApplied);
            StringBuilder builder = new();
            builder.AppendLine("SENTINEL_AI_EVIDENCE_V1");
            builder.AppendLine($"purpose: {Sanitize(purpose, ref redactionApplied)}");
            if (!string.IsNullOrWhiteSpace(sanitizedQuestion)) builder.AppendLine($"question: {Limit(sanitizedQuestion, 500)}");
            builder.AppendLine("rules: use only supplied verified evidence; distinguish fact from inference; do not authorize repairs; request more evidence only if Sentinel cannot collect it locally.");
            builder.AppendLine("facts:");

            foreach (string fact in facts.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string line = "- " + Limit(Sanitize(fact, ref redactionApplied), 1_400);
                if (builder.Length + line.Length + 2 > characterBudget) break;
                builder.AppendLine(line);
            }

            string payload = builder.ToString().Trim();
            return new AiEvidencePackage(purpose, payload, payload.Length,
                Math.Max(1, (int)Math.Ceiling(payload.Length / 4.0)), redactionApplied, false);
        }

        private static void Add(ICollection<string> facts, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) facts.Add($"{label}: {value.Trim()}");
        }

        internal static string SanitizeForCloud(string? value)
        {
            bool ignored = false;
            return Sanitize(value, ref ignored);
        }

        private static string Sanitize(string? value, ref bool redactionApplied)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string original = value;
            string result = value;

            result = Regex.Replace(result, @"(?im)\bAuthorization\s*:\s*[^\r\n]+", "Authorization: [redacted]");
            result = Regex.Replace(result, @"(?im)\b(?:token|api[_ -]?key|password|passwd|secret|client[_ -]?secret)\s*[:=]\s*(?:\"[^\"\r\n]*\"|'[^'\r\n]*'|[^\r\n,;]+)", "credential=[redacted]");
            result = Regex.Replace(result, @"(?i)\bBearer\s+[A-Za-z0-9._~+\-/]+=*", "Bearer [redacted]");
            result = Regex.Replace(result, @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", "[redacted-token]");
            result = Regex.Replace(result, @"(?i)\b(?:[A-Z]:\\Users\\)[^\\\s]+", @"C:\Users\[redacted-user]");
            result = Regex.Replace(result, @"(?i)\b(?:user(name)?|account)\s*[:=]\s*(?:\"[^\"\r\n]*\"|'[^'\r\n]*'|[^\s,;]+)", "user=[redacted]");
            result = Regex.Replace(result, @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[redacted-email]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\b[A-Fa-f0-9]{2}(?:[:-][A-Fa-f0-9]{2}){5}\b", "[redacted-mac]");
            result = Regex.Replace(result, @"(?i)\b(?:device|machine|hardware|serial)[ _-]?(?:id|identifier|number)?\s*[:=]\s*[^\s,;]+", "device-id=[redacted]");
            result = RedactIpLiterals(result);
            result = Regex.Replace(result, @"\s+", " ").Trim();

            if (!string.Equals(result, original, StringComparison.Ordinal)) redactionApplied = true;
            return result;
        }

        private static string RedactIpLiterals(string input)
        {
            // Candidate tokenization followed by IPAddress.TryParse avoids treating arbitrary
            // colon-containing text as IPv6 while covering IPv4 and IPv6 literals.
            return Regex.Replace(input, @"(?<![A-Za-z0-9])\[?[0-9A-Fa-f:.%]{2,}\]?(?![A-Za-z0-9])", match =>
            {
                string candidate = match.Value.Trim('[', ']');
                string addressPart = candidate;
                int zone = addressPart.IndexOf('%');
                if (zone >= 0) addressPart = addressPart[..zone];
                return IPAddress.TryParse(addressPart, out _) ? "[redacted-ip]" : match.Value;
            });
        }

        private static string RedactEndpoint(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return "[unknown-endpoint]";
            string value = endpoint.Trim();
            int port = 0;
            if (value.StartsWith("[", StringComparison.Ordinal) && value.Contains("]:", StringComparison.Ordinal))
            {
                int separator = value.LastIndexOf(':');
                int.TryParse(value[(separator + 1)..], out port);
            }
            else
            {
                int separator = value.LastIndexOf(':');
                if (separator > 0 && value.Count(c => c == ':') == 1) int.TryParse(value[(separator + 1)..], out port);
            }
            return port > 0 ? $"[redacted-endpoint]:{port}" : "[redacted-endpoint]";
        }

        private static string Availability(bool available) => available ? "active" : "unavailable";
        private static string Limit(string value, int max) => value.Length <= max ? value : value[..max] + "…";
    }

    public sealed record AiEvidencePackage(string Purpose, string Payload, int CharacterCount, int EstimatedInputTokens, bool Redacted, bool ContainsFullSystemDump);
}
