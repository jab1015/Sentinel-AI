/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;
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
            Add(facts, "reason", snapshot.InvestigationReasonCode);
            Add(facts, "conclusion", snapshot.InvestigationConclusion);
            Add(facts, "summary", snapshot.InvestigationSummary);
            Add(facts, "guidance", snapshot.GuidanceEvidence);

            if (snapshot.FlaggedProcessCount > 0)
                Add(facts, "process", $"{snapshot.PrimaryFlaggedProcessName}: {snapshot.PrimaryFlaggedProcessReason}");
            if (snapshot.FlaggedConnectionCount > 0)
                Add(facts, "network", $"{snapshot.PrimaryFlaggedConnectionProcessName} -> {RedactEndpoint(snapshot.PrimaryFlaggedConnectionRemoteEndpoint)}: {snapshot.PrimaryFlaggedConnectionReason}");
            if (snapshot.FlaggedServiceCount > 0) Add(facts, "service", snapshot.PrimaryFlaggedServiceName);
            if (snapshot.FlaggedCommandLineCount > 0) Add(facts, "command", $"{snapshot.PrimaryCommandLineProcessName}: {snapshot.PrimaryCommandLineReason}");
            if (snapshot.FlaggedStartupEntryCount > 0) Add(facts, "startup", $"{snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}");
            if (snapshot.FlaggedScheduledTaskCount > 0) Add(facts, "task", $"{snapshot.PrimaryFlaggedScheduledTaskName}: {snapshot.PrimaryFlaggedScheduledTaskReason}");
            if (!snapshot.DefenderEnabled || !snapshot.FirewallEnabled)
                Add(facts, "protection", $"Defender={snapshot.DefenderEnabled}; Firewall={snapshot.FirewallEnabled}; {snapshot.ProtectionHealthSummary}");
            if (snapshot.RecentCrashDetected)
                Add(facts, "windows-crash", snapshot.RecentCrashSummary);

            if (external is not null)
            {
                Add(facts, "external-topic", external.Topic);
                Add(facts, "external-summary", external.Summary);
                if (external.MatchedTerms.Count > 0) Add(facts, "external-matches", string.Join(", ", external.MatchedTerms.Take(10)));
                if (external.Sources.Count > 0) Add(facts, "authorities", string.Join(", ", external.Sources.Where(x => x.Reached).Select(x => x.SourceName).Distinct().Take(5)));
            }

            Add(facts, "machine-specific supplemental evidence", supplementalEvidence);

            string sanitizedQuestion = Sanitize(userQuestion);
            StringBuilder builder = new();
            builder.AppendLine("SENTINEL_AI_EVIDENCE_V1");
            builder.AppendLine($"purpose: {Sanitize(purpose)}");
            if (!string.IsNullOrWhiteSpace(sanitizedQuestion)) builder.AppendLine($"question: {Limit(sanitizedQuestion, 500)}");
            builder.AppendLine("rules: use only supplied verified evidence; distinguish fact from inference; do not authorize repairs; request more evidence only if Sentinel cannot collect it locally.");
            builder.AppendLine("facts:");

            foreach (string fact in facts.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string line = "- " + Limit(Sanitize(fact), 1_400);
                if (builder.Length + line.Length + 2 > characterBudget) break;
                builder.AppendLine(line);
            }

            string payload = builder.ToString().Trim();
            return new AiEvidencePackage(purpose, payload, payload.Length, Math.Max(1, (int)Math.Ceiling(payload.Length / 4.0)), true, false);
        }

        private static void Add(ICollection<string> facts, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) facts.Add($"{label}: {value.Trim()}");
        }

        private static string Sanitize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string result = value;
            result = Regex.Replace(result, @"(?i)\b(?:[A-Z]:\\Users\\)[^\\\s]+", @"C:\Users\[redacted-user]");
            result = Regex.Replace(result, @"(?i)\b(?:user(name)?|account)\s*[:=]\s*[^\s,;]+", "user=[redacted]");
            result = Regex.Replace(result, @"(?i)\b(?:token|api[_ -]?key|authorization|password|passwd|secret)\s*[:=]\s*[^\s,;]+", "$1=[redacted]");
            result = Regex.Replace(result, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[redacted-ip]");
            result = Regex.Replace(result, @"\b[A-Fa-f0-9]{2}(?:[:-][A-Fa-f0-9]{2}){5}\b", "[redacted-mac]");
            return Regex.Replace(result, @"\s+", " ").Trim();
        }

        private static string RedactEndpoint(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return "[unknown-endpoint]";
            int lastColon = endpoint.LastIndexOf(':');
            string port = lastColon >= 0 && lastColon < endpoint.Length - 1 ? endpoint[(lastColon + 1)..] : string.Empty;
            return string.IsNullOrWhiteSpace(port) ? "[redacted-endpoint]" : $"[redacted-endpoint]:{port}";
        }

        private static string Limit(string value, int max) => value.Length <= max ? value : value[..max] + "â€¦";
    }

    public sealed record AiEvidencePackage(string Purpose, string Payload, int CharacterCount, int EstimatedInputTokens, bool Redacted, bool ContainsFullSystemDump);
}

