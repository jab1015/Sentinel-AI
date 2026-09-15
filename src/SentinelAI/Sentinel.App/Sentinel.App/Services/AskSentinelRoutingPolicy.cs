/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Chooses the next Ask Sentinel reasoning layer from the user's intent rather
    /// than requiring an exact phrase match. Local deterministic evidence stays first;
    /// Basic AI is the normal language fallback; fresh/current claims go to research.
    /// </summary>
    public sealed class AskSentinelRoutingPolicy
    {
        public AskSentinelRoute Decide(string question, bool localAnswerInsufficient)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            string value = Normalize(question);

            bool freshResearch = RequiresFreshExternalResearch(value);
            bool explanation = NeedsNaturalLanguageExplanation(value);
            bool clearlyLocal = IsClearlyLocalStateQuestion(value);
            bool generalQuestion = LooksLikeGeneralQuestion(value);
            bool ambiguousLocalKeywordMatch = !clearlyLocal && generalQuestion && !localAnswerInsufficient;
            bool useBasicAi = !freshResearch && (localAnswerInsufficient || explanation || ambiguousLocalKeywordMatch);

            return new AskSentinelRoute(
                UseBasicAi: useBasicAi,
                UseExternalResearch: freshResearch,
                ExplanationRequested: explanation || ambiguousLocalKeywordMatch,
                Reason: freshResearch
                    ? "The question depends on current or explicitly requested external information."
                    : localAnswerInsufficient
                        ? "Deterministic local handlers did not fully answer the question; use Basic AI."
                        : explanation
                            ? "The user asked for explanation or interpretation beyond a terse local status value."
                            : ambiguousLocalKeywordMatch
                                ? "A broad local keyword matched, but the wording looks like a general question rather than a request for this PC's current state."
                                : "Verified local evidence directly answers the question.");
        }

        public AiEscalationContext CreateBasicAiContext(string question)
        {
            string value = Normalize(question);
            bool highRisk = ContainsAny(value,
                "security", "malware", "virus", "ransomware", "spyware", "hack", "breach",
                "firewall", "credential", "password", "phishing", "rootkit", "trojan");
            bool highComplexity = question.Length > 180 || ContainsAny(value,
                "compare", "analyze", "root cause", "why", "explain", "multiple", "several", "walk me through");

            return new AiEscalationContext(
                LocalEvidenceAvailable: true,
                LocalEvidenceInsufficient: true,
                LocalConclusionVerified: false,
                CachedVerifiedFindingAvailable: false,
                ExternalResearchApplicable: false,
                AuthoritativeResearchAttempted: false,
                AuthoritativeExternalConclusionVerified: false,
                NeedsInterpretation: true,
                NeedsUserExplanation: true,
                HighComplexity: highComplexity,
                HighRisk: highRisk);
        }

        public AiEscalationContext CreateExternalAiContext(
            string question,
            string topic,
            int sourceCount,
            bool authoritativeConclusionVerified)
        {
            string value = Normalize(question);
            bool highRisk = topic.Equals("security", StringComparison.OrdinalIgnoreCase) ||
                            topic.Equals("firewall", StringComparison.OrdinalIgnoreCase) ||
                            ContainsAny(value, "malware", "virus", "ransomware", "breach", "hack", "credential", "phishing");

            return new AiEscalationContext(
                LocalEvidenceAvailable: true,
                LocalEvidenceInsufficient: true,
                LocalConclusionVerified: false,
                CachedVerifiedFindingAvailable: false,
                ExternalResearchApplicable: true,
                AuthoritativeResearchAttempted: true,
                AuthoritativeExternalConclusionVerified: authoritativeConclusionVerified,
                NeedsInterpretation: true,
                NeedsUserExplanation: true,
                HighComplexity: sourceCount > 1 || question.Length > 180,
                HighRisk: highRisk);
        }

        public static bool RequiresFreshExternalResearch(string question)
        {
            string value = Normalize(question);
            return ContainsAny(value,
                "search online", "search the internet", "look online", "look it up", "external source", "external sources",
                "authoritative source", "official source", "official documentation", "microsoft says", "vendor says", "manufacturer says",
                "latest", "today", "right now", "current version", "current release", "release notes", "known issue", "known issues",
                "cve", "security advisory", "research this", "check online", "check the internet", "web search", "search the web");
        }

        internal static bool NeedsNaturalLanguageExplanation(string question)
        {
            string value = Normalize(question);

            if (ContainsAny(value,
                "what does", "what do ", "how does", "how do ", "how can", "how should", "why is", "why does", "why did",
                "explain", "tell me about", "help me understand", "difference between", "what's the difference", "whats the difference",
                "meaning of", "what does it mean", "walk me through"))
                return true;

            bool definitionQuestion = value.StartsWith("what is ", StringComparison.Ordinal) ||
                                      value.StartsWith("what are ", StringComparison.Ordinal) ||
                                      value.StartsWith("who is ", StringComparison.Ordinal) ||
                                      value.StartsWith("who are ", StringComparison.Ordinal);
            if (!definitionQuestion) return false;

            // Preserve fast deterministic answers for direct questions about this PC's
            // current state while still letting definitions such as "What is TPM?" use AI.
            return !IsClearlyLocalStateQuestion(value);
        }

        internal static bool IsClearlyLocalStateQuestion(string question)
        {
            string value = Normalize(question);

            if (ContainsAny(value,
                "my computer", "my pc", "this computer", "this pc", "on this computer", "on this pc",
                "on my computer", "on my pc", "current status", "status of my", "status on this",
                "right now", "currently", "what is my ", "what are my ", "what's my ", "whats my ",
                "using now", "using right now", "usage now", "usage right now", "installed on", "running on",
                "enabled on", "disabled on", "flagged on", "detected on", "found on"))
                return true;

            if (ContainsAny(value,
                "is defender on", "is defender enabled", "defender status",
                "is firewall on", "is firewall enabled", "firewall status",
                "is bitlocker on", "is bitlocker enabled", "bitlocker status",
                "is secure boot on", "is secure boot enabled", "secure boot status",
                "is tpm on", "is tpm enabled", "tpm status",
                "windows update status", "check for updates", "pending restart", "restart pending",
                "cpu usage", "memory usage", "ram usage", "disk usage", "storage usage",
                "running processes", "running services", "startup apps", "scheduled tasks"))
                return true;

            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 3 && ContainsAny(value,
                "cpu", "memory", "ram", "disk", "storage", "defender", "firewall", "bitlocker", "secure boot",
                "tpm", "windows update", "drivers", "driver", "startup apps", "services", "processes", "network"))
                return true;

            return false;
        }

        internal static bool LooksLikeGeneralQuestion(string question)
        {
            string value = Normalize(question);
            return value.EndsWith("?", StringComparison.Ordinal) ||
                   StartsWithAny(value,
                       "what ", "which ", "why ", "how ", "who ", "when ", "where ",
                       "can ", "could ", "would ", "should ", "is ", "are ", "does ", "do ",
                       "tell me ", "explain ", "recommend ", "compare ") ||
                   ContainsAny(value,
                       "best ", "better ", "recommend", "safe to", "pros and cons", "advantages", "disadvantages");
        }

        private static bool StartsWithAny(string value, params string[] prefixes) =>
            prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        private static string Normalize(string value) =>
            string.Join(' ', value.Trim().ToLowerInvariant().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

        private static bool ContainsAny(string value, params string[] terms) =>
            terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public sealed record AskSentinelRoute(
        bool UseBasicAi,
        bool UseExternalResearch,
        bool ExplanationRequested,
        string Reason);
}
